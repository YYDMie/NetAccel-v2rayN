using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetAccel.Managed.Api;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Startup;

namespace NetAccel.Managed.Presentation;

public enum ManagedLoginStage
{
    Default,
    SigningIn,
    PreparingDevice,
    SyncingConfiguration,
    Ready,
    NoAssignment,
    InvalidCredentials,
    NetworkError,
    AccountDisabled,
    InstanceRevoked,
    MandatoryUpdate,
    SyncUnavailable,
    Faulted,
}

public sealed class ManagedLoginViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAuthService _auth;
    private readonly IManagedStartupOrchestrator _startup;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _operationCts;
    private string _username = string.Empty;
    private ManagedLoginStage _stage = ManagedLoginStage.Default;
    private string? _requestId;
    private bool _disposed;

    public ManagedLoginViewModel(IAuthService auth, IManagedStartupOrchestrator startup)
    {
        _auth = auth;
        _startup = startup;
        _startup.ProgressChanged += Startup_ProgressChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public ManagedLoginStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsLoginFormVisible));
            OnPropertyChanged(nameof(IsPrimaryActionVisible));
            OnPropertyChanged(nameof(IsCancelVisible));
            OnPropertyChanged(nameof(IsReturnToLoginVisible));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public string Title => Stage switch
    {
        ManagedLoginStage.Default => "登录 NetAccel",
        ManagedLoginStage.SigningIn => "正在登录",
        ManagedLoginStage.PreparingDevice => "正在准备当前设备",
        ManagedLoginStage.SyncingConfiguration => "正在获取线路",
        ManagedLoginStage.Ready => "当前设备已就绪",
        ManagedLoginStage.NoAssignment => "暂无可用线路",
        ManagedLoginStage.InvalidCredentials => "账号或密码不正确",
        ManagedLoginStage.NetworkError => "暂时无法连接服务",
        ManagedLoginStage.AccountDisabled => "当前账号已停用",
        ManagedLoginStage.InstanceRevoked => "当前设备已停用",
        ManagedLoginStage.MandatoryUpdate => "需要更新客户端",
        ManagedLoginStage.SyncUnavailable => "线路暂未同步完成",
        _ => "暂时无法完成登录",
    };

    public string Message => Stage switch
    {
        ManagedLoginStage.Default => "登录后会自动获取管理员分配给当前设备的线路。",
        ManagedLoginStage.SigningIn => "正在验证账号信息，请稍候。",
        ManagedLoginStage.PreparingDevice => "正在绑定并检查当前设备，无需额外操作。",
        ManagedLoginStage.SyncingConfiguration => "正在安全同步可用线路。",
        ManagedLoginStage.Ready => "线路已经同步，可以开始网络加速。",
        ManagedLoginStage.NoAssignment => "当前设备还没有分配线路，请稍后重新检查。",
        ManagedLoginStage.InvalidCredentials => "请检查账号和密码后重新输入。",
        ManagedLoginStage.NetworkError => "请检查网络连接，然后重试。",
        ManagedLoginStage.AccountDisabled => "请联系管理员确认账号状态。",
        ManagedLoginStage.InstanceRevoked => "请退出账号或联系管理员重新启用设备。",
        ManagedLoginStage.MandatoryUpdate => "当前版本过旧，更新后才能继续使用。",
        ManagedLoginStage.SyncUnavailable => "安全配置或线路数据尚未准备好，请重新检查。",
        ManagedLoginStage.Faulted when !string.IsNullOrWhiteSpace(_requestId)
            => $"发生了未知问题，可在诊断中提供请求编号 {_requestId}。",
        _ => "请重试；如果问题持续出现，请打开诊断。",
    };

    public string PrimaryActionText => Stage switch
    {
        ManagedLoginStage.Default or ManagedLoginStage.InvalidCredentials => "登录",
        ManagedLoginStage.MandatoryUpdate => "重新检查",
        ManagedLoginStage.Ready => "进入首页",
        _ => "重新检查",
    };

    public bool IsBusy => Stage is ManagedLoginStage.SigningIn
        or ManagedLoginStage.PreparingDevice
        or ManagedLoginStage.SyncingConfiguration;

    public bool IsLoginFormVisible => Stage is ManagedLoginStage.Default
        or ManagedLoginStage.InvalidCredentials;

    public bool IsPrimaryActionVisible => !IsBusy
        && Stage is not ManagedLoginStage.AccountDisabled
        && Stage is not ManagedLoginStage.InstanceRevoked;

    public bool IsCancelVisible => IsBusy;

    public bool IsReturnToLoginVisible => !IsBusy
        && Stage is ManagedLoginStage.AccountDisabled or ManagedLoginStage.InstanceRevoked;

    public bool CanSubmit => !IsBusy
        && (!IsLoginFormVisible || !string.IsNullOrWhiteSpace(Username));

    public bool IsReady => Stage == ManagedLoginStage.Ready;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!await TryEnterOperationAsync(ct))
        {
            return;
        }

        try
        {
            await RunStartupAsync(ct);
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task LoginAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(password))
        {
            Stage = ManagedLoginStage.InvalidCredentials;
            return;
        }

        if (!await TryEnterOperationAsync(ct))
        {
            return;
        }

        try
        {
            Stage = ManagedLoginStage.SigningIn;
            var result = await _auth.LoginAsync(Username.Trim(), password, _operationCts!.Token);
            if (!result.IsSuccess)
            {
                ApplyAuthFailure(result);
                return;
            }

            Stage = ManagedLoginStage.PreparingDevice;
            await RunStartupAsync(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            Stage = ManagedLoginStage.Default;
        }
        catch (ManagedOperationCancelledException)
        {
            Stage = ManagedLoginStage.Default;
        }
        catch
        {
            Stage = ManagedLoginStage.Faulted;
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task RetryAsync(CancellationToken ct = default)
    {
        if (IsLoginFormVisible)
        {
            return;
        }

        await InitializeAsync(ct);
    }

    public void Cancel()
    {
        _operationCts?.Cancel();
    }

    public async Task ReturnToLoginAsync(CancellationToken ct = default)
    {
        if (!await TryEnterOperationAsync(ct))
        {
            return;
        }

        try
        {
            await _auth.LogoutAsync(_operationCts!.Token);
            Stage = ManagedLoginStage.Default;
        }
        catch (ManagedOperationCancelledException)
        {
            Stage = ManagedLoginStage.Default;
        }
        catch
        {
            Stage = ManagedLoginStage.Faulted;
        }
        finally
        {
            ExitOperation();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startup.ProgressChanged -= Startup_ProgressChanged;
        _operationCts?.Cancel();
        _operationCts?.Dispose();
    }

    private async Task RunStartupAsync(CancellationToken ct)
    {
        try
        {
            var token = _operationCts?.Token ?? ct;
            var result = await _startup.StartupAsync(token);
            _requestId = result.RequestId;

            if (result.State == ManagedStartupState.Ready)
            {
                var config = await _startup.GetCurrentConfigAsync();
                Stage = config == null ? ManagedLoginStage.SyncUnavailable : ManagedLoginStage.Ready;
                return;
            }

            Stage = result.State switch
            {
                ManagedStartupState.NeedsLogin => ManagedLoginStage.Default,
                ManagedStartupState.NoAssignment => ManagedLoginStage.NoAssignment,
                ManagedStartupState.InstanceRevoked => ManagedLoginStage.InstanceRevoked,
                ManagedStartupState.AccountDisabled => ManagedLoginStage.AccountDisabled,
                ManagedStartupState.MandatoryUpdate => ManagedLoginStage.MandatoryUpdate,
                ManagedStartupState.Offline => ManagedLoginStage.NetworkError,
                _ => ManagedLoginStage.Faulted,
            };
        }
        catch (OperationCanceledException)
        {
            Stage = ManagedLoginStage.Default;
        }
        catch (ManagedOperationCancelledException)
        {
            Stage = ManagedLoginStage.Default;
        }
    }

    private void ApplyAuthFailure(AuthResult result)
    {
        _requestId = result.RequestId;
        Stage = result.Kind switch
        {
            AuthResultKind.BadCredentials or AuthResultKind.SessionRevoked => ManagedLoginStage.InvalidCredentials,
            AuthResultKind.AccountDisabled => ManagedLoginStage.AccountDisabled,
            AuthResultKind.NetworkError => ManagedLoginStage.NetworkError,
            _ => ManagedLoginStage.Faulted,
        };
    }

    private async Task<bool> TryEnterOperationAsync(CancellationToken ct)
    {
        if (!await _operationGate.WaitAsync(0, ct))
        {
            return false;
        }

        _operationCts?.Dispose();
        _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return true;
    }

    private void ExitOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        _operationGate.Release();
    }

    private void Startup_ProgressChanged(ManagedStartupPhase phase)
    {
        Stage = phase == ManagedStartupPhase.SyncingConfiguration
            ? ManagedLoginStage.SyncingConfiguration
            : ManagedLoginStage.PreparingDevice;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
