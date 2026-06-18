namespace v2rayN.Managed.Services;

public static class ManagedClassicModeBridge
{
    public static Func<Task>? ReturnToManagedAsync { get; set; }

    public static Task ReturnAsync()
        => ReturnToManagedAsync?.Invoke() ?? Task.CompletedTask;
}
