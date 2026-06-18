using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Runtime;

public enum ManagedDiagnosticCondition
{
    Normal,
    Stopped,
    Warning,
    Error,
}

public sealed record ManagedDiagnosticCheck
{
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string Detail { get; init; }
    public required ManagedDiagnosticCondition Condition { get; init; }
    public bool NeedsRepair { get; init; }
}

public sealed record ManagedDiagnosticsSnapshot
{
    public required ManagedDiagnosticCheck ServiceConnection { get; init; }
    public required ManagedDiagnosticCheck CurrentRoute { get; init; }
    public required ManagedDiagnosticCheck LocalNetwork { get; init; }
    public required ManagedDiagnosticCheck AccelerationEngine { get; init; }
    public bool ClassicModeOwnsConnection { get; init; }

    public IReadOnlyList<ManagedDiagnosticCheck> Checks =>
    [
        ServiceConnection,
        CurrentRoute,
        LocalNetwork,
        AccelerationEngine,
    ];

    public bool NeedsRepair => Checks.Any(check => check.NeedsRepair);
}

public sealed record ManagedRepairStepResult
{
    public required string Name { get; init; }
    public required bool Success { get; init; }
    public required string Message { get; init; }
}

public sealed record ManagedRepairResult
{
    public required IReadOnlyList<ManagedRepairStepResult> Steps { get; init; }
    public required ManagedDiagnosticsSnapshot Snapshot { get; init; }
    public bool BlockedByClassicMode { get; init; }
    public bool Success => !BlockedByClassicMode && Steps.All(step => step.Success);
}

public sealed record ManagedCoreRuntimeSnapshot
{
    public bool IsRunning { get; init; }
    public bool HasManagedRuntime { get; init; }
    public bool HasManagedSystemProxy { get; init; }
    public bool HasManagedTun { get; init; }
}

public interface IManagedDiagnosticsRuntime
{
    ManagedCoreRuntimeSnapshot Inspect();
    Task ClearResidualCoreAsync(CancellationToken ct = default);
    Task RestoreManagedSystemProxyAsync(CancellationToken ct = default);
    Task ClearManagedTunStateAsync(CancellationToken ct = default);
}

public interface IManagedDiagnosticsService
{
    Task<ManagedDiagnosticsSnapshot> CheckAsync(CancellationToken ct = default);
    Task<ManagedRepairResult> RepairAsync(CancellationToken ct = default);
}

public sealed class ManagedDiagnosticsService : IManagedDiagnosticsService
{
    private readonly IManagedConnectionCoordinator _connection;
    private readonly IManagedDiagnosticsRuntime _runtime;
    private readonly ConnectionOwnershipCoordinator _ownership;
    private readonly Func<bool> _serviceReady;
    private readonly Func<Task<ManagedConfigPayload?>> _configProvider;
    private readonly Func<CancellationToken, Task<bool>> _resync;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ManagedDiagnosticsService(
        IManagedConnectionCoordinator connection,
        IManagedDiagnosticsRuntime runtime,
        ConnectionOwnershipCoordinator ownership,
        Func<bool> serviceReady,
        Func<Task<ManagedConfigPayload?>> configProvider,
        Func<CancellationToken, Task<bool>> resync)
    {
        _connection = connection;
        _runtime = runtime;
        _ownership = ownership;
        _serviceReady = serviceReady;
        _configProvider = configProvider;
        _resync = resync;
    }

    public async Task<ManagedDiagnosticsSnapshot> CheckAsync(CancellationToken ct = default)
    {
        var payload = await _configProvider();
        var owner = await _ownership.InspectAsync(ct);
        return BuildSnapshot(payload, owner, _connection.Status, _runtime.Inspect());
    }

    public async Task<ManagedRepairResult> RepairAsync(CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct);
        try
        {
            var before = await CheckAsync(ct);
            if (before.ClassicModeOwnsConnection)
            {
                return new ManagedRepairResult
                {
                    BlockedByClassicMode = true,
                    Steps =
                    [
                        Failed("自动修复", "经典模式正在使用系统网络，请先在经典窗口停止连接。"),
                    ],
                    Snapshot = before,
                };
            }

            var steps = new List<ManagedRepairStepResult>();
            if (before.CurrentRoute.NeedsRepair
                || before.LocalNetwork.NeedsRepair
                || before.AccelerationEngine.NeedsRepair)
            {
                if (_connection.Status.State is ManagedConnectionState.Connected
                    or ManagedConnectionState.Starting
                    or ManagedConnectionState.Stopping
                    or ManagedConnectionState.Faulted)
                {
                    var stop = await _connection.StopAsync(ct);
                    steps.Add(stop.Success
                        ? Passed("停止托管连接", "已释放托管连接状态。")
                        : Failed("停止托管连接", "托管连接未能完整停止，继续尝试安全清理。"));
                }

                steps.Add(await RunStepAsync(
                    "清理残留核心",
                    "残留核心已停止。",
                    token => _runtime.ClearResidualCoreAsync(token),
                    ct));
                steps.Add(await RunStepAsync(
                    "恢复系统代理",
                    "托管模式修改的系统代理已恢复。",
                    token => _runtime.RestoreManagedSystemProxyAsync(token),
                    ct));
                steps.Add(await RunStepAsync(
                    "清理失效 TUN 状态",
                    "失效的托管 TUN 状态已清理。",
                    token => _runtime.ClearManagedTunStateAsync(token),
                    ct));
            }
            else
            {
                steps.Add(Passed("本机网络", "未发现需要清理的托管网络状态。"));
            }

            steps.Add(await ResyncAsync(ct));
            var after = await CheckAsync(ct);
            return new ManagedRepairResult
            {
                Steps = steps,
                Snapshot = after,
            };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private ManagedDiagnosticsSnapshot BuildSnapshot(
        ManagedConfigPayload? payload,
        ConnectionOwnershipSnapshot? owner,
        ManagedConnectionStatus connection,
        ManagedCoreRuntimeSnapshot runtime)
    {
        var classicOwnsConnection = owner?.Owner == ConnectionOwner.Classic;
        var service = _serviceReady()
            ? Normal("服务连接", "正常", "账号和当前设备已通过服务检查。")
            : Error("服务连接", "异常", "当前服务会话不可用，请重新检查。");

        var route = BuildRouteCheck(payload, connection);
        var localNetwork = BuildLocalNetworkCheck(connection, runtime, classicOwnsConnection);
        var engine = BuildEngineCheck(connection, runtime, classicOwnsConnection);

        return new ManagedDiagnosticsSnapshot
        {
            ServiceConnection = service,
            CurrentRoute = route,
            LocalNetwork = localNetwork,
            AccelerationEngine = engine,
            ClassicModeOwnsConnection = classicOwnsConnection,
        };
    }

    private static ManagedDiagnosticCheck BuildRouteCheck(
        ManagedConfigPayload? payload,
        ManagedConnectionStatus connection)
    {
        var available = payload?.Profiles.Where(profile => profile.Available).ToList() ?? [];
        if (available.Count == 0)
        {
            return Error("当前线路", "不可用", "当前设备没有已同步且可用的线路。");
        }

        if (connection.State == ManagedConnectionState.Connected
            && !string.IsNullOrWhiteSpace(connection.EffectiveProfileId))
        {
            var active = available.FirstOrDefault(profile =>
                string.Equals(profile.Id, connection.EffectiveProfileId, StringComparison.Ordinal));
            if (active == null)
            {
                return Error("当前线路", "不可用", "正在使用的线路已不在当前授权集合中。", needsRepair: true);
            }

            return Normal("当前线路", "正常", $"当前使用：{SafeDisplayName(active)}。");
        }

        return Normal("当前线路", "正常", $"已同步 {available.Count} 条可用线路。");
    }

    private static ManagedDiagnosticCheck BuildLocalNetworkCheck(
        ManagedConnectionStatus connection,
        ManagedCoreRuntimeSnapshot runtime,
        bool classicOwnsConnection)
    {
        if (classicOwnsConnection)
        {
            return Normal("本机网络", "正常", "经典模式正在使用系统网络，托管诊断不会修改它。");
        }

        var expectsManagedNetwork = connection.State is ManagedConnectionState.Connected
            or ManagedConnectionState.Starting
            or ManagedConnectionState.Stopping;
        var hasResidualState = !expectsManagedNetwork
            && (runtime.HasManagedSystemProxy || runtime.HasManagedTun || runtime.HasManagedRuntime);
        var inconsistentActiveState = connection.State == ManagedConnectionState.Connected
            && (!runtime.IsRunning || !runtime.HasManagedRuntime);
        var restoreFailed = connection.FailureKind == ManagedConnectionFailureKind.RestoreFailed;

        return hasResidualState || inconsistentActiveState || restoreFailed
            ? Error("本机网络", "需要修复", "检测到未完整恢复的托管网络状态。", needsRepair: true)
            : Normal("本机网络", "正常", "未发现托管代理或虚拟网络残留。");
    }

    private static ManagedDiagnosticCheck BuildEngineCheck(
        ManagedConnectionStatus connection,
        ManagedCoreRuntimeSnapshot runtime,
        bool classicOwnsConnection)
    {
        if (classicOwnsConnection)
        {
            return runtime.IsRunning
                ? Normal("加速引擎", "运行中", "经典模式正在使用加速引擎。")
                : Stopped("加速引擎", "已停止", "经典模式当前未运行加速引擎。");
        }

        var expectsRunning = connection.State is ManagedConnectionState.Connected
            or ManagedConnectionState.Starting;
        if (runtime.IsRunning && expectsRunning)
        {
            return Normal("加速引擎", "运行中", "托管加速引擎正在正常运行。");
        }

        if (!runtime.IsRunning && !expectsRunning)
        {
            return Stopped("加速引擎", "已停止", "当前没有运行中的托管加速引擎。");
        }

        return Error("加速引擎", "异常", "加速引擎状态与当前连接状态不一致。", needsRepair: true);
    }

    private async Task<ManagedRepairStepResult> ResyncAsync(CancellationToken ct)
    {
        try
        {
            var ready = await _resync(ct);
            var config = await _configProvider();
            return ready && config != null
                ? Passed("重新同步配置", "已重新同步当前有效的托管配置。")
                : Failed("重新同步配置", "配置暂未同步完成，未使用过期配置。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed("重新同步配置", "配置同步失败，现有账号和本地数据未被修改。");
        }
    }

    private static async Task<ManagedRepairStepResult> RunStepAsync(
        string name,
        string successMessage,
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        try
        {
            await action(ct);
            return Passed(name, successMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed(name, $"{name}未完成，请查看详细信息。");
        }
    }

    private static string SafeDisplayName(ManagedProfile profile)
        => string.IsNullOrWhiteSpace(profile.DisplayName) ? "已分配线路" : profile.DisplayName.Trim();

    private static ManagedDiagnosticCheck Normal(string title, string status, string detail)
        => new()
        {
            Title = title,
            Status = status,
            Detail = detail,
            Condition = ManagedDiagnosticCondition.Normal,
        };

    private static ManagedDiagnosticCheck Stopped(string title, string status, string detail)
        => new()
        {
            Title = title,
            Status = status,
            Detail = detail,
            Condition = ManagedDiagnosticCondition.Stopped,
        };

    private static ManagedDiagnosticCheck Error(
        string title,
        string status,
        string detail,
        bool needsRepair = false)
        => new()
        {
            Title = title,
            Status = status,
            Detail = detail,
            Condition = ManagedDiagnosticCondition.Error,
            NeedsRepair = needsRepair,
        };

    private static ManagedRepairStepResult Passed(string name, string message)
        => new() { Name = name, Success = true, Message = message };

    private static ManagedRepairStepResult Failed(string name, string message)
        => new() { Name = name, Success = false, Message = message };
}
