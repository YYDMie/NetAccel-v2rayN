namespace NetAccel.Managed.Domain;

public enum ProfileSource
{
    Managed,
    Local,
    LegacySubscription,
}

public enum ProfileOperation
{
    Connect,
    Select,
    Edit,
    Delete,
    Copy,
    Share,
    Export,
    Backup,
    Diagnose,
}

public static class ProfileSourcePolicy
{
    public static bool Can(ProfileSource source, ProfileOperation operation)
    {
        return source switch
        {
            ProfileSource.Managed => operation is ProfileOperation.Connect
                or ProfileOperation.Select
                or ProfileOperation.Diagnose,
            ProfileSource.Local => true,
            ProfileSource.LegacySubscription => operation is not ProfileOperation.Share,
            _ => false,
        };
    }

    public static void EnsureAllowed(ProfileSource source, ProfileOperation operation)
    {
        if (!Can(source, operation))
        {
            throw new InvalidOperationException($"{source} profiles do not allow {operation}.");
        }
    }
}
