namespace NetAccel.Managed.Runtime;

public static class PackagedCoreBootstrapper
{
    private static readonly (string Directory, string FileName)[] Cores =
    [
        ("xray", "xray.exe"),
        ("sing_box", "sing-box.exe"),
    ];

    public static void Install(string packageRoot, string dataRoot)
    {
        foreach (var (directory, fileName) in Cores)
        {
            var source = Path.Combine(packageRoot, "bin", directory, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Packaged core is missing: {directory}/{fileName}", source);
            }

            var destinationDirectory = Path.Combine(dataRoot, "bin", directory);
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, fileName);
            var temporary = destination + ".new";
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
    }
}
