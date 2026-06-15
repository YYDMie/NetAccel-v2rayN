using System.Text.RegularExpressions;
using NetAccel.Managed.Auth;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Api;

public interface IManagedConfigService
{
    Task<ConfigAckResult> AckConfigAsync(
        int revision,
        string status,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        string? errorCode = null,
        string? errorDetail = null,
        CancellationToken ct = default);
}

public enum ConfigAckResultKind
{
    Success,
    Revoked,
    AccountDisabled,
    NetworkError,
    UnknownError,
}

public sealed class ConfigAckResult
{
    public ConfigAckResultKind Kind { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
    public bool IsSuccess => Kind == ConfigAckResultKind.Success;
}

public sealed class ManagedConfigService : IManagedConfigService
{
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly IAuthService _auth;
    private readonly Func<string, Task>? _logAsync;

    public ManagedConfigService(ManagedApiClient api, ICredentialVault vault, IAuthService auth, Func<string, Task>? logAsync = null)
    {
        _api = api;
        _vault = vault;
        _auth = auth;
        _logAsync = logAsync;
    }

    public async Task<ConfigAckResult> AckConfigAsync(
        int revision,
        string status,
        string clientVersion,
        Dictionary<string, string> coreVersions,
        string? errorCode = null,
        string? errorDetail = null,
        CancellationToken ct = default)
    {
        var instanceCredential = await _vault.RetrieveAsync(CredentialVaultEntry.InstanceCredential);
        if (string.IsNullOrEmpty(instanceCredential))
        {
            return new ConfigAckResult { Kind = ConfigAckResultKind.UnknownError, ErrorCode = "missing_identity" };
        }

        var request = new ManagedConfigAckRequest
        {
            Status = status,
            ClientVersion = clientVersion,
            CoreVersions = coreVersions,
            ErrorCode = errorCode,
            ErrorDetail = AckSanitizer.Sanitize(errorDetail),
        };

        var opResult = await _auth.ExecuteWithRefreshAsync(
            async (token, ctInner) => await _api.PostAsync<ManagedConfigAck>(
                $"/client/managed/config/{revision}/ack",
                request,
                accessToken: token,
                instanceCredential: instanceCredential,
                ct: ctInner),
            ct);

        if (opResult.IsSuccess)
        {
            return new ConfigAckResult { Kind = ConfigAckResultKind.Success };
        }

        if (opResult.RefreshResult != null)
        {
            var kind = opResult.RefreshResult.Kind switch
            {
                AuthResultKind.AccountDisabled => ConfigAckResultKind.AccountDisabled,
                AuthResultKind.NetworkError => ConfigAckResultKind.NetworkError,
                _ => ConfigAckResultKind.UnknownError,
            };
            return new ConfigAckResult { Kind = kind, ErrorCode = opResult.RefreshResult.ErrorCode, RequestId = opResult.RefreshResult.RequestId };
        }

        if (opResult.OperationException is { } ex)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientInstanceRevoked => ConfigAckResultKind.Revoked,
                ManagedErrorCode.ClientAccountInactive => ConfigAckResultKind.AccountDisabled,
                _ => ConfigAckResultKind.UnknownError,
            };
            return new ConfigAckResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }

        return new ConfigAckResult { Kind = ConfigAckResultKind.UnknownError, ErrorCode = "missing_identity" };
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }
}

/// <summary>
/// Sanitizes ACK error detail to avoid leaking secrets.
/// </summary>
internal static class AckSanitizer
{
    private static readonly Regex UriPattern = new(@"https?://[^\s,;]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NamedPortPattern = new(@"\bport\s*(?:=|:)\s*\d{1,5}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ColonPortPattern = new(@"(?<=:)\d{1,5}\b", RegexOptions.Compiled);
    private static readonly Regex HostPattern = new(@"[a-zA-Z0-9][-a-zA-Z0-9]*\.[a-zA-Z0-9][-a-zA-Z0-9.]*", RegexOptions.Compiled);
    private static readonly Regex IpPattern = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);

    internal static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var s = input.Length > 1024 ? input[..1024] : input;

        if (s.Contains('{') || s.Contains('}') || s.Contains('[') || s.Contains(']'))
        {
            return "[redacted]";
        }

        s = UriPattern.Replace(s, "[redacted]");
        s = NamedPortPattern.Replace(s, "[redacted]");
        s = ColonPortPattern.Replace(s, "[redacted]");
        s = IpPattern.Replace(s, "[redacted]");
        s = HostPattern.Replace(s, "[redacted]");

        // Replace potential secrets with [redacted]
        s = s.Replace("endpoint", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("host", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("port", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("credential", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("token", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("key", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("ciphertext", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("plaintext", "[redacted]", StringComparison.OrdinalIgnoreCase);
        s = s.Replace("config", "[redacted]", StringComparison.OrdinalIgnoreCase);

        return s;
    }
}
