using System.Security.Cryptography;
using System.Text.Json;
using NetAccel.Managed.Update;

var options = ParseArgs(args);
var artifactPath = Required(options, "artifact");
var artifactUrl = Required(options, "url");
var version = Required(options, "version");
var outputPath = Path.GetFullPath(Required(options, "output"));
var arch = options.GetValueOrDefault("arch", "x86_64");
var channel = options.GetValueOrDefault("channel", "stable");
var minVersion = options.GetValueOrDefault("min-version");
var keyId = Environment.GetEnvironmentVariable("NETACCEL_RELEASE_SIGNING_KEY_ID");
var privateKeyPem = Environment.GetEnvironmentVariable("NETACCEL_RELEASE_SIGNING_KEY_PEM");

if (!File.Exists(artifactPath) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(privateKeyPem))
{
    throw new InvalidOperationException("Artifact and release signing environment are required.");
}
if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("Artifact URL must use HTTPS.");
}

await using var artifact = File.OpenRead(artifactPath);
var sha = Convert.ToHexString(await SHA256.HashDataAsync(artifact)).ToLowerInvariant();
var manifest = new ManagedReleaseManifest
{
    ClientProduct = ManagedReleaseVerifier.Product,
    Platform = "windows",
    Arch = arch,
    Channel = channel,
    Version = version,
    ArtifactUrl = artifactUrl,
    Sha256 = sha,
    Signature = string.Empty,
    SignatureAlgorithm = ManagedReleaseVerifier.SignatureAlgorithm,
    SignatureKeyId = keyId,
    MinVersion = string.IsNullOrWhiteSpace(minVersion) ? null : minVersion,
};

using var signer = ECDsa.Create();
signer.ImportFromPem(privateKeyPem.Replace("\\n", Environment.NewLine, StringComparison.Ordinal));
manifest = manifest with
{
    Signature = Convert.ToBase64String(signer.SignData(
        ManagedReleaseVerifier.BuildSignaturePreimage(manifest),
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    WriteIndented = true,
}));
Console.WriteLine(outputPath);

static Dictionary<string, string> ParseArgs(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i += 2)
    {
        if (i + 1 >= values.Length || !values[i].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Arguments must use --name value pairs.");
        }
        result[values[i][2..]] = values[i + 1];
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> values, string name)
    => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"--{name} is required.");
