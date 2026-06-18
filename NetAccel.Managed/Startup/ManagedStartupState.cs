namespace NetAccel.Managed.Startup;

public enum ManagedStartupPhase
{
    CheckingCredentials,
    RefreshingSession,
    BindingInstance,
    RegisteringDeviceKey,
    CheckingControlState,
    SyncingConfiguration,
}

public enum ManagedStartupState
{
    NeedsLogin,
    Ready,
    NoAssignment,
    InstanceRevoked,
    AccountDisabled,
    MandatoryUpdate,
    Offline,
    Faulted,
}

public sealed class ManagedStartupResult
{
    public ManagedStartupState State { get; init; }
    public string? Message { get; init; }
    public string? RequestId { get; init; }
}
