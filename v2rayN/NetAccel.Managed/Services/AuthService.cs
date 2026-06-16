using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;
using NetAccel.Managed.Storage;

namespace NetAccel.Managed.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<bool> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    event EventHandler<AuthStateChangedEventArgs>? StateChanged;
}

public sealed class AuthStateChangedEventArgs : EventArgs
{
    public bool IsAuthenticated { get; }
    public string? ErrorCode { get; }
    public AuthStateChangedEventArgs(bool isAuthenticated, string? errorCode = null)
    {
        IsAuthenticated = isAuthenticated;
        ErrorCode = errorCode;
    }
}

public sealed class AuthService : IAuthService
{
    private readonly IManagedApiClient _apiClient;
    private readonly ICredentialVault _vault;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private TaskCompletionSource<bool>? _refreshTcs;

    public AuthService(IManagedApiClient apiClient, ICredentialVault vault)
    {
        _apiClient = apiClient;
        _vault = vault;
        _apiClient.OnRefreshTokenAsync = OnRefreshTokenAsync;
    }

    public event EventHandler<AuthStateChangedEventArgs>? StateChanged;

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            var request = new LoginRequest { Username = username, Password = password };
            var response = await _apiClient.PostAsync<LoginResponse>("/api/v1/client/login", request, false, allowRetry: false, ct);
            if (response == null) return false;

            await _vault.SetAsync("access_token", response.AccessToken, ct);
            await _vault.SetAsync("refresh_token", response.RefreshToken, ct);
            _apiClient.SetAccessToken(response.AccessToken);
            StateChanged?.Invoke(this, new AuthStateChangedEventArgs(true));
            return true;
        }
        catch (ManagedApiError ex)
        {
            StateChanged?.Invoke(this, new AuthStateChangedEventArgs(false, ex.ErrorCode));
            return false;
        }
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        TaskCompletionSource<bool>? tcs;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_refreshTcs != null)
            {
                tcs = _refreshTcs;
                return await tcs.Task.WaitAsync(ct);
            }

            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshTcs = tcs;
        }
        finally
        {
            _refreshLock.Release();
        }

        bool result;
        try
        {
            result = await DoRefreshAsync(ct);
            tcs.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            tcs.TrySetCanceled();
            throw;
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            await _refreshLock.WaitAsync(CancellationToken.None);
            try
            {
                if (_refreshTcs == tcs)
                {
                    _refreshTcs = null;
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        return result;
    }

    public Task<bool> OnRefreshTokenAsync()
    {
        return RefreshAsync(CancellationToken.None);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await _apiClient.PostAsync<object>("/api/v1/client/logout", null, false, allowRetry: false, ct);
        }
        catch
        {
            // Best effort
        }
        finally
        {
            await ClearAsync(ct);
        }
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var token = await _vault.GetAsync("access_token", ct);
        return !string.IsNullOrEmpty(token);
    }

    private async Task<bool> DoRefreshAsync(CancellationToken ct)
    {
        try
        {
            var refreshToken = await _vault.GetAsync("refresh_token", ct);
            if (string.IsNullOrEmpty(refreshToken)) return false;

            var request = new RefreshRequest { RefreshToken = refreshToken };
            var response = await _apiClient.PostAsync<LoginResponse>("/api/v1/client/refresh", request, false, allowRetry: false, ct);
            if (response == null) return false;

            await _vault.SetAsync("access_token", response.AccessToken, ct);
            await _vault.SetAsync("refresh_token", response.RefreshToken, ct);
            _apiClient.SetAccessToken(response.AccessToken);
            StateChanged?.Invoke(this, new AuthStateChangedEventArgs(true));
            return true;
        }
        catch (ManagedApiError ex)
        {
            if (ex.ErrorCode is "refresh_token_invalid" or "client_auth_session_revoked" or "client_account_inactive")
            {
                await ClearAsync(ct);
            }
            StateChanged?.Invoke(this, new AuthStateChangedEventArgs(false, ex.ErrorCode));
            return false;
        }
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        await _vault.DeleteAsync("access_token", ct);
        await _vault.DeleteAsync("refresh_token", ct);
        _apiClient.SetAccessToken(null);
        StateChanged?.Invoke(this, new AuthStateChangedEventArgs(false));
    }
}
