using System.IO.Compression;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Presentation;
using NetAccel.Managed.Runtime;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedDiagnosticsServiceTests
{
    [Fact]
    public async Task CheckAsync_HealthyConnectedState_ReturnsFourReadOnlyChecks()
    {
        var runtime = new FakeDiagnosticsRuntime
        {
            Snapshot = new ManagedCoreRuntimeSnapshot
            {
                IsRunning = true,
                HasManagedRuntime = true,
                HasManagedSystemProxy = true,
            },
        };
        var connection = new FakeCoordinator(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            EffectiveProfileId = "plan-42",
            NetworkMode = ManagedConnectionMode.SystemProxy,
        });
        await using var ownership = CreateOwnership();
        var service = CreateService(connection, runtime, ownership);

        var snapshot = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, snapshot.Checks.Count);
        Assert.Equal("正常", snapshot.ServiceConnection.Status);
        Assert.Equal("正常", snapshot.CurrentRoute.Status);
        Assert.Equal("正常", snapshot.LocalNetwork.Status);
        Assert.Equal("运行中", snapshot.AccelerationEngine.Status);
        Assert.False(snapshot.NeedsRepair);
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task CheckAsync_UsesOnlyFriendlyRouteFields()
    {
        var payload = CreatePayload();
        payload.Profiles[0].Endpoint.Host = "private.example";
        payload.Profiles[0].Endpoint.Port = 443;
        var connection = new FakeCoordinator(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            EffectiveProfileId = "plan-42",
        });
        await using var ownership = CreateOwnership();
        var service = CreateService(
            connection,
            new FakeDiagnosticsRuntime
            {
                Snapshot = new ManagedCoreRuntimeSnapshot
                {
                    IsRunning = true,
                    HasManagedRuntime = true,
                    HasManagedSystemProxy = true,
                },
            },
            ownership,
            payload);

        var snapshot = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Contains("香港智能通道", snapshot.CurrentRoute.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("private.example", snapshot.CurrentRoute.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("443", snapshot.CurrentRoute.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairAsync_ResidualManagedState_RunsOnlyWhitelistedActionsInOrder()
    {
        var runtime = new FakeDiagnosticsRuntime
        {
            Snapshot = new ManagedCoreRuntimeSnapshot
            {
                IsRunning = true,
                HasManagedRuntime = true,
                HasManagedSystemProxy = true,
                HasManagedTun = true,
            },
        };
        var connection = new FakeCoordinator();
        var resyncCalls = 0;
        await using var ownership = CreateOwnership();
        var service = CreateService(
            connection,
            runtime,
            ownership,
            resync: _ =>
            {
                resyncCalls++;
                return Task.FromResult(true);
            });

        var result = await service.RepairAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(
            ["clear_core", "restore_proxy", "clear_tun"],
            runtime.Calls);
        Assert.Equal(1, resyncCalls);
        Assert.DoesNotContain(result.Steps, step =>
            step.Name.Contains("账号", StringComparison.Ordinal)
            || step.Name.Contains("节点", StringComparison.Ordinal)
            || step.Name.Contains("订阅", StringComparison.Ordinal));
        Assert.False(result.Snapshot.NeedsRepair);
    }

    [Fact]
    public async Task RepairAsync_ConnectedButBroken_StopsCoordinatorBeforeRuntimeCleanup()
    {
        var runtime = new FakeDiagnosticsRuntime
        {
            Snapshot = new ManagedCoreRuntimeSnapshot
            {
                IsRunning = false,
                HasManagedRuntime = true,
                HasManagedSystemProxy = true,
            },
        };
        var connection = new FakeCoordinator(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            EffectiveProfileId = "plan-42",
        });
        await using var ownership = CreateOwnership();
        var service = CreateService(connection, runtime, ownership);

        var result = await service.RepairAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, connection.StopCalls);
        Assert.Equal(
            ["clear_core", "restore_proxy", "clear_tun"],
            runtime.Calls);
    }

    [Fact]
    public async Task RepairAsync_ActiveRouteNoLongerAuthorized_StopsBeforeResync()
    {
        var runtime = new FakeDiagnosticsRuntime
        {
            Snapshot = new ManagedCoreRuntimeSnapshot
            {
                IsRunning = true,
                HasManagedRuntime = true,
                HasManagedSystemProxy = true,
            },
        };
        var connection = new FakeCoordinator(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Connected,
            EffectiveProfileId = "revoked-plan",
        });
        await using var ownership = CreateOwnership();
        var service = CreateService(connection, runtime, ownership);

        var before = await service.CheckAsync(TestContext.Current.CancellationToken);
        var result = await service.RepairAsync(TestContext.Current.CancellationToken);

        Assert.True(before.CurrentRoute.NeedsRepair);
        Assert.Equal(1, connection.StopCalls);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RepairAsync_WhenClassicOwnsConnection_PerformsNoWrites()
    {
        var store = new InMemoryConnectionOwnershipStore();
        await store.WriteAsync(new ConnectionOwnershipSnapshot
        {
            Owner = ConnectionOwner.Classic,
            ProcessId = Environment.ProcessId,
            MachineName = Environment.MachineName,
            AcquiredAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, TestContext.Current.CancellationToken);
        await using var ownership = new ConnectionOwnershipCoordinator(
            store,
            useGlobalMutex: false,
            isProcessRunning: _ => true);
        var runtime = new FakeDiagnosticsRuntime
        {
            Snapshot = new ManagedCoreRuntimeSnapshot { IsRunning = true },
        };
        var connection = new FakeCoordinator();
        var resyncCalls = 0;
        var service = CreateService(
            connection,
            runtime,
            ownership,
            resync: _ =>
            {
                resyncCalls++;
                return Task.FromResult(true);
            });

        var result = await service.RepairAsync(TestContext.Current.CancellationToken);

        Assert.True(result.BlockedByClassicMode);
        Assert.False(result.Success);
        Assert.Empty(runtime.Calls);
        Assert.Equal(0, connection.StopCalls);
        Assert.Equal(0, resyncCalls);
    }

    [Fact]
    public async Task ViewModel_RefreshDoesNotInvokeRepairActions()
    {
        var service = new FakeDiagnosticsService();
        using var viewModel = new ManagedDiagnosticsViewModel(service);

        await viewModel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.CheckCalls);
        Assert.Equal(0, service.RepairCalls);
        Assert.Equal("正常", viewModel.ServiceConnection.Status);
        Assert.True(viewModel.CanRepair);
    }

    [Fact]
    public async Task ExportAsync_WritesOnlyAllowlistedRedactedEntries()
    {
        using var temp = new TempDirectory();
        var destination = Path.Combine(temp.Path, "diagnostics.zip");
        var payload = CreatePayload();
        payload.Profiles[0].VlessCredentials = new VlessCredentials
        {
            Uuid = "11111111-2222-3333-4444-555555555555",
        };
        var connection = new FakeCoordinator(new ManagedConnectionStatus
        {
            State = ManagedConnectionState.Faulted,
            EffectiveProfileId = "plan-42",
            FailureKind = ManagedConnectionFailureKind.Unknown,
            Message = "Authorization=Bearer secret-token private.example:443",
        });
        var configReads = 0;
        await using var ownership = CreateOwnership();
        var service = new ManagedDiagnosticsService(
            connection,
            new FakeDiagnosticsRuntime
            {
                Snapshot = new ManagedCoreRuntimeSnapshot
                {
                    HasManagedRuntime = true,
                    HasManagedSystemProxy = true,
                },
            },
            ownership,
            () => true,
            () =>
            {
                configReads++;
                return Task.FromResult<ManagedConfigPayload?>(payload);
            },
            _ => Task.FromResult(true),
            clientVersion: "8.0-test",
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.ExportAsync(destination, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(0, configReads);
        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal(["diagnostics.json", "privacy.txt"], archive.Entries.Select(entry => entry.FullName).Order());
        var contents = string.Join(
            "\n",
            archive.Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }));
        Assert.Contains("netaccel-diagnostics/v1", contents, StringComparison.Ordinal);
        Assert.Contains("8.0-test", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private.example", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("11111111-2222-3333-4444-555555555555", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan-42", contents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization=", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exporter_RejectsNonZipDestinationWithoutWritingFile()
    {
        using var temp = new TempDirectory();
        var destination = Path.Combine(temp.Path, "diagnostics.json");
        var exporter = new ManagedDiagnosticsExporter();

        await Assert.ThrowsAsync<ArgumentException>(() => exporter.ExportAsync(
            destination,
            CreateExportData(),
            TestContext.Current.CancellationToken));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Exporter_WriteFailureReturnsFriendlyResultAndRemovesTemporaryArchive()
    {
        using var temp = new TempDirectory();
        var destination = Path.Combine(temp.Path, "diagnostics.zip");
        Directory.CreateDirectory(destination);
        var exporter = new ManagedDiagnosticsExporter();

        var result = await exporter.ExportAsync(
            destination,
            CreateExportData(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("诊断包导出失败，未写入账号或线路数据。", result.Message);
        Assert.Empty(Directory.EnumerateFiles(temp.Path));
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public async Task ViewModel_ExportReportsFriendlyResult()
    {
        var service = new FakeDiagnosticsService();
        using var viewModel = new ManagedDiagnosticsViewModel(service);

        await viewModel.ExportAsync("diagnostics.zip", TestContext.Current.CancellationToken);

        Assert.Equal(1, service.ExportCalls);
        Assert.Equal("脱敏诊断包已导出。", viewModel.Summary);
        Assert.True(viewModel.CanExport);
    }

    [Fact]
    public async Task ViewModel_ExportFailureIsFriendlyAndDoesNotRunRepair()
    {
        var service = new FakeDiagnosticsService { ThrowOnExport = true };
        using var viewModel = new ManagedDiagnosticsViewModel(service);

        await viewModel.ExportAsync("diagnostics.zip", TestContext.Current.CancellationToken);

        Assert.Equal(1, service.ExportCalls);
        Assert.Equal(0, service.RepairCalls);
        Assert.Equal("诊断包导出失败，账号和线路数据未被写入。", viewModel.Summary);
    }

    private static ManagedDiagnosticsService CreateService(
        FakeCoordinator connection,
        FakeDiagnosticsRuntime runtime,
        ConnectionOwnershipCoordinator ownership,
        ManagedConfigPayload? payload = null,
        Func<CancellationToken, Task<bool>>? resync = null)
    {
        payload ??= CreatePayload();
        return new ManagedDiagnosticsService(
            connection,
            runtime,
            ownership,
            () => true,
            () => Task.FromResult<ManagedConfigPayload?>(payload),
            resync ?? (_ => Task.FromResult(true)));
    }

    private static ConnectionOwnershipCoordinator CreateOwnership()
        => new(
            new InMemoryConnectionOwnershipStore(),
            useGlobalMutex: false,
            isProcessRunning: _ => true);

    private static ManagedConfigPayload CreatePayload()
        => new()
        {
            PayloadSchema = "managed-config/v1",
            AssignmentRevision = 12,
            SelectionRevision = 4,
            RecommendedProfileId = "plan-42",
            Profiles =
            [
                new ManagedProfile
                {
                    Id = "plan-42",
                    DisplayName = "香港智能通道",
                    Available = true,
                    Endpoint = new EndpointInfo
                    {
                        Host = "private.example",
                        Port = 443,
                    },
                    Policy = new ProfilePolicy
                    {
                        AllowSystemProxy = true,
                        AllowTun = true,
                    },
                },
            ],
            ClientPolicy = new ClientPolicy
            {
                AllowTun = true,
                AllowAutomaticFailover = true,
            },
        };

    private static ManagedDiagnosticsExportData CreateExportData()
    {
        var normal = new ManagedDiagnosticCheck
        {
            Title = "检查",
            Status = "正常",
            Detail = "正常",
            Condition = ManagedDiagnosticCondition.Normal,
        };
        return new ManagedDiagnosticsExportData
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ClientVersion = "test",
            Snapshot = new ManagedDiagnosticsSnapshot
            {
                ServiceConnection = normal with { Title = "服务连接" },
                CurrentRoute = normal with { Title = "当前线路" },
                LocalNetwork = normal with { Title = "本机网络" },
                AccelerationEngine = normal with { Title = "加速引擎" },
            },
            Connection = new ManagedConnectionStatus(),
            Runtime = new ManagedCoreRuntimeSnapshot(),
            Owner = ConnectionOwner.None,
        };
    }

    private sealed class FakeDiagnosticsRuntime : IManagedDiagnosticsRuntime
    {
        public ManagedCoreRuntimeSnapshot Snapshot { get; set; } = new();
        public List<string> Calls { get; } = [];

        public ManagedCoreRuntimeSnapshot Inspect() => Snapshot;

        public Task ClearResidualCoreAsync(CancellationToken ct = default)
        {
            Calls.Add("clear_core");
            Snapshot = Snapshot with { IsRunning = false };
            return Task.CompletedTask;
        }

        public Task RestoreManagedSystemProxyAsync(CancellationToken ct = default)
        {
            Calls.Add("restore_proxy");
            Snapshot = Snapshot with { HasManagedSystemProxy = false };
            return Task.CompletedTask;
        }

        public Task ClearManagedTunStateAsync(CancellationToken ct = default)
        {
            Calls.Add("clear_tun");
            Snapshot = Snapshot with
            {
                HasManagedTun = false,
                HasManagedRuntime = false,
            };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoordinator : IManagedConnectionCoordinator
    {
        public FakeCoordinator(ManagedConnectionStatus? status = null)
        {
            Status = status ?? new ManagedConnectionStatus();
        }

        public event Action<ManagedConnectionStatus>? StatusChanged;

        public ManagedConnectionStatus Status { get; private set; }
        public int StopCalls { get; private set; }

        public Task<ManagedConnectionResult> StartAsync(
            ManagedConnectionStartRequest request,
            CancellationToken ct = default)
            => Task.FromResult(ManagedConnectionResult.FromStatus(Status));

        public Task<ManagedConnectionResult> StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            Status = new ManagedConnectionStatus { State = ManagedConnectionState.Ready };
            StatusChanged?.Invoke(Status);
            return Task.FromResult(ManagedConnectionResult.FromStatus(Status));
        }
    }

    private sealed class FakeDiagnosticsService : IManagedDiagnosticsService
    {
        public int CheckCalls { get; private set; }
        public int RepairCalls { get; private set; }
        public int ExportCalls { get; private set; }
        public bool ThrowOnExport { get; init; }

        public Task<ManagedDiagnosticsSnapshot> CheckAsync(CancellationToken ct = default)
        {
            CheckCalls++;
            return Task.FromResult(CreateSnapshot());
        }

        public Task<ManagedRepairResult> RepairAsync(CancellationToken ct = default)
        {
            RepairCalls++;
            return Task.FromResult(new ManagedRepairResult
            {
                Steps = [],
                Snapshot = CreateSnapshot(),
            });
        }

        public Task<ManagedDiagnosticsExportResult> ExportAsync(
            string destinationPath,
            CancellationToken ct = default)
        {
            ExportCalls++;
            if (ThrowOnExport)
            {
                throw new IOException("injected export failure");
            }

            return Task.FromResult(new ManagedDiagnosticsExportResult
            {
                Success = true,
                Message = "脱敏诊断包已导出。",
                FileName = "diagnostics.zip",
            });
        }

        private static ManagedDiagnosticsSnapshot CreateSnapshot()
        {
            var normal = new ManagedDiagnosticCheck
            {
                Title = "检查",
                Status = "正常",
                Detail = "正常",
                Condition = ManagedDiagnosticCondition.Normal,
            };
            return new ManagedDiagnosticsSnapshot
            {
                ServiceConnection = normal with { Title = "服务连接" },
                CurrentRoute = normal with { Title = "当前线路" },
                LocalNetwork = normal with { Title = "本机网络" },
                AccelerationEngine = normal with
                {
                    Title = "加速引擎",
                    Status = "已停止",
                    Condition = ManagedDiagnosticCondition.Stopped,
                },
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netaccel-diagnostics-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
