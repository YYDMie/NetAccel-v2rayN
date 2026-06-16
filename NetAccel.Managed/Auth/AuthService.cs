using System.Net;
using System.Net.Http.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Vault;

namespace NetAccel.Managed.Auth;

/// <summary>
/// Managed authentication service: login, refresh (with concurrency lock), logout.
/// Tokens are stored only through the vault; never in config, SQLite, logs, or ViewModels.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<bool> HasCredentialsAsync();
    Task<string?> GetAccessTokenAsync();
    Task<int?> GetAccountIdAsync();

    /// <summary>
    /// Execute an authenticated operation with automatic one-time refresh on HTTP 401.
    /// Never loops. Does not refresh on stable non-recoverable auth errors.
    /// </summary>
    Task<AuthOperationResult<T>> ExecuteWithRefreshAsync<T>(
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken ct = default);
}

/// <summary>
/// Result of <see cref="IAuthService.ExecuteWithRefreshAsync{T}"/>.
/// </summary>
public sealed class AuthOperationResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public AuthResult? RefreshResult { get; init; }
    public ManagedApiException? OperationException { get; init; }
}

public enum AuthResultKind
{
    Success,
    BadCredentials,
    AccountDisabled,
    SessionRevoked,
    NetworkError,
    UnknownError,
}

public sealed class AuthResult
{
    public AuthResultKind Kind { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
    public bool IsSuccess => Kind == AuthResultKind.Success;
}

public sealed class AuthService : IAuthService
{
    private readonly ManagedApiClient _api;
    private readonly ICredentialVault _vault;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Func<string, Task>? _logAsync;

    public AuthService(ManagedApiClient api, ICredentialVault vault, Func<string, Task>? logAsync = null)
    {
        _api = api;
        _vault = vault;
        _logAsync = logAsync;
    }

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            var result = await _api.PostAsync<LoginResponse>("/client/login", new LoginRequest
            {
                Username = username,
                Password = password,
            }, ct: ct);

            await _vault.StoreAsync(CredentialVaultEntry.AccessToken, result.AccessToken);
            await _vault.StoreAsync(CredentialVaultEntry.RefreshToken, result.RefreshToken);
            await _vault.StoreAsync(CredentialVaultEntry.AccountId, result.AccountId.ToString());

            return new AuthResult { Kind = AuthResultKind.Success };
        }
        catch (ManagedApiException ex) when (ex.HttpStatusCode == 401 || ex.HttpStatusCode == 403)
        {
            var kind = ex.ErrorCode switch
            {
                ManagedErrorCode.ClientAccountInactive => AuthResultKind.AccountDisabled,
                ManagedErrorCode.ClientAuthSessionRevoked => AuthResultKind.SessionRevoked,
                _ => AuthResultKind.BadCredentials,
            };
            return new AuthResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
        }
        catch (ManagedNetworkTimeoutException)
        {
            return new AuthResult { Kind = AuthResultKind.NetworkError };
        }
        catch (ManagedOperationCancelledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await LogAsync($"Login unexpected error: {ex.GetType().Name}");
            return new AuthResult { Kind = AuthResultKind.UnknownError };
        }
    }

    public async Task<AuthResult> RefreshAsync(CancellationToken ct = default)
    {
        // Concurrency lock: parallel callers perform at most one refresh
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            await _refreshLock.WaitAsync(ct);
            try
            {
                // Another caller completed the refresh; just validate token exists
                var token = await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
                return token != null
                    ? new AuthResult { Kind = AuthResultKind.Success }
                    : new AuthResult { Kind = AuthResultKind.UnknownError };
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        try
        {
            var refreshToken = await _vault.RetrieveAsync(CredentialVaultEntry.RefreshToken);
            if (string.IsNullOrEmpty(refreshToken))
            {
                return new AuthResult { Kind = AuthResultKind.BadCredentials, ErrorCode = ManagedErrorCode.RefreshTokenRequired };
            }

            try
            {
                var result = await _api.PostAsync<RefreshResponse>("/client/refresh", new RefreshRequest
                {
                    RefreshToken = refreshToken,
                }, ct: ct);

                await _vault.StoreAsync(CredentialVaultEntry.AccessToken, result.AccessToken);
                await _vault.StoreAsync(CredentialVaultEntry.RefreshToken, result.RefreshToken);
                await _vault.StoreAsync(CredentialVaultEntry.AccountId, result.AccountId.ToString());

                return new AuthResult { Kind = AuthResultKind.Success };
            }
            catch (ManagedApiException ex) when (ex.HttpStatusCode == 401 || ex.HttpStatusCode == 403)
            {
                var kind = ex.ErrorCode switch
                {
                    ManagedErrorCode.ClientAccountInactive => AuthResultKind.AccountDisabled,
                    ManagedErrorCode.RefreshTokenInvalid => AuthResultKind.BadCredentials,
                    ManagedErrorCode.ClientAuthSessionRevoked => AuthResultKind.SessionRevoked,
                    _ => AuthResultKind.BadCredentials,
                };
                // Clear credentials on unrecoverable refresh failure
                if (kind == AuthResultKind.BadCredentials || kind == AuthResultKind.AccountDisabled || kind == AuthResultKind.SessionRevoked)
                {
                    await ClearCredentialsAsync();
                }
                return new AuthResult { Kind = kind, ErrorCode = ex.ErrorCode, RequestId = ex.RequestId };
            }
            catch (ManagedNetworkTimeoutException)
            {
                return new AuthResult { Kind = AuthResultKind.NetworkError };
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var accessToken = await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
        if (!string.IsNullOrEmpty(accessToken))
        {
            try
            {
                await _api.PostAsync<object>("/client/logout", null, accessToken: accessToken, ct: ct);
            }
            catch (ManagedApiException) { /* best effort */ }
            catch (ManagedNetworkTimeoutException) { /* best effort */ }
        }
        await ClearCredentialsAsync();
    }

    public async Task<bool> HasCredentialsAsync()
    {
        var refresh = await _vault.RetrieveAsync(CredentialVaultEntry.RefreshToken);
        return !string.IsNullOrEmpty(refresh);
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        return await _vault.RetrieveAsync(CredentialVaultEntry.AccessToken);
    }

    public async Task<int?> GetAccountIdAsync()
    {
        var raw = await _vault.RetrieveAsync(CredentialVaultEntry.AccountId);
        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var id))
        {
            return id;
        }
        return null;
    }

    public async Task<AuthOperationResult<T>> ExecuteWithRefreshAsync<T>(
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
        {
            return new AuthOperationResult<T> { IsSuccess = false };
        }

        try
        {
            var result = await operation(accessToken, ct);
            return new AuthOperationResult<T> { IsSuccess = true, Value = result };
        }
        catch (ManagedApiException ex) when (ex.HttpStatusCode == 401)
        {
            if (IsNonRecoverableAuthError(ex.ErrorCode))
            {
                return new AuthOperationResult<T> { IsSuccess = false, OperationException = ex };
            }

            var refreshResult = await RefreshAsync(ct);
            if (!refreshResult.IsSuccess)
            {
                return new AuthOperationResult<T> { IsSuccess = false, RefreshResult = refreshResult };
            }

            var newToken = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(newToken))
            {
                return new AuthOperationResult<T> { IsSuccess = false, RefreshResult = refreshResult };
            }

            try
            {
                var retryResult = await operation(newToken, ct);
                return new AuthOperationResult<T> { IsSuccess = true, Value = retryResult };
            }
            catch (ManagedApiException retryEx)
            {
                return new AuthOperationResult<T> { IsSuccess = false, OperationException = retryEx };
            }
        }
        catch (ManagedApiException ex)
        {
            return new AuthOperationResult<T> { IsSuccess = false, OperationException = ex };
        }
    }

    private static bool IsNonRecoverableAuthError(string errorCode) => errorCode switch
    {
        ManagedErrorCode.ClientAccountInactive => true,
        ManagedErrorCode.ClientInstanceRevoked => true,
        ManagedErrorCode.InstanceCredentialInvalid => true,
        ManagedErrorCode.InstanceCredentialRequired => true,
        ManagedErrorCode.InstanceCredentialScopeDenied => true,
        ManagedErrorCode.InstanceCredentialInstanceMismatch => true,
        ManagedErrorCode.ManagedIdentityMismatch => true,
        ManagedErrorCode.ManagedAuthIdentityMismatch => true,
        ManagedErrorCode.ManagedScopeDenied => true,
        ManagedErrorCode.ClientAuthSessionRevoked => true,
        _ => false,
    };

    private async Task ClearCredentialsAsync()
    {
        await _vault.DeleteAsync(CredentialVaultEntry.AccessToken);
        await _vault.DeleteAsync(CredentialVaultEntry.RefreshToken);
        await _vault.DeleteAsync(CredentialVaultEntry.AccountId);
    }

    private async Task LogAsync(string message)
    {
        if (_logAsync != null)
        {
            await _logAsync(message);
        }
    }
}
