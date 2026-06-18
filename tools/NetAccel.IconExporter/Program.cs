using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [STAThread]
    private static void Main(string[] args)
    {
        var repositoryRoot = args.Length > 0
            ? Path.GetFullPath(args[0])
            : FindRepositoryRoot(AppContext.BaseDirectory);
        var managedResources = Path.Combine(repositoryRoot, "v2rayN", "v2rayN", "Managed", "Resources");
        var iconOutput = Path.Combine(repositoryRoot, "v2rayN", "v2rayN", "Resources");

        var application = new Application();
        application.Resources.MergedDictionaries.Add(LoadDictionary(Path.Combine(managedResources, "Colors.xaml")));
        application.Resources.MergedDictionaries.Add(LoadDictionary(Path.Combine(managedResources, "NetAccelIcon.xaml")));
        application.Resources.MergedDictionaries.Add(LoadDictionary(Path.Combine(managedResources, "NetAccelTrayIcons.xaml")));

        WriteIcon(
            Path.Combine(iconOutput, "NetAccel.ico"),
            Sizes.Select(size => Render(GetDrawing(application, size <= 24 ? "NetAccelIconSmall" : "NetAccelIcon"), size)));
        WriteIcon(Path.Combine(iconOutput, "NotifyIcon1.ico"), Sizes.Select(size => Render(GetDrawing(application, "NetAccelTrayIdle"), size)));
        WriteIcon(Path.Combine(iconOutput, "NotifyIcon2.ico"), Sizes.Select(size => Render(GetDrawing(application, "NetAccelTrayConnected"), size)));
        WriteIcon(Path.Combine(iconOutput, "NotifyIcon3.ico"), Sizes.Select(size => Render(GetDrawing(application, "NetAccelTrayFaulted"), size)));
        WriteIcon(Path.Combine(iconOutput, "NotifyIcon4.ico"), Sizes.Select(size => Render(GetDrawing(application, "NetAccelTrayStartingBase"), size)));

        Console.WriteLine($"Exported NetAccel application and tray icons to {iconOutput}");
    }

    private static DrawingImage GetDrawing(Application application, string key)
    {
        return application.TryFindResource(key) as DrawingImage
               ?? throw new InvalidOperationException($"DrawingImage resource '{key}' was not found.");
    }

    private static ResourceDictionary LoadDictionary(string path)
    {
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    private static byte[] Render(DrawingImage source, int size)
    {
        var image = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        image.Measure(new Size(size, size));
        image.Arrange(new Rect(0, 0, size, size));
        image.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(image);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void WriteIcon(string path, IEnumerable<byte[]> frames)
    {
        var frameList = frames.ToList();
        if (frameList.Count != Sizes.Length)
        {
            throw new InvalidOperationException("ICO frame count does not match the configured sizes.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frameList.Count);

        var offset = 6 + (16 * frameList.Count);
        for (var index = 0; index < frameList.Count; index++)
        {
            var size = Sizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frameList[index].Length);
            writer.Write((uint)offset);
            offset += frameList[index].Length;
        }

        foreach (var frame in frameList)
        {
            writer.Write(frame);
        }
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing global.json.");
    }
}
