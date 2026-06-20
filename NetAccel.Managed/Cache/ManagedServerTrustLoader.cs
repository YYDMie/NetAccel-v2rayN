namespace NetAccel.Managed.Cache;

public static class ManagedServerTrustLoader
{
    public static string? Load(
        string startupPath,
        string? inlinePublicKey = null,
        string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(inlinePublicKey))
        {
            return inlinePublicKey.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
        }

        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(startupPath, "managed-trust", "envelope.pem")
            : configuredPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}
