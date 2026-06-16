using System.Net;
using NetAccel.Managed.Dtos;

namespace NetAccel.Managed.Api;

/// <summary>
/// Result type for GET requests with ETag support.
/// Allows callers to distinguish 304 Not Modified from 200 with null data.
/// </summary>
public sealed class ETagResponse<T>
{
    public T? Data { get; init; }
    public bool IsNotModified { get; init; }
}

public interface IManagedApiClient
{
    Task<T?> PostAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default);
    Task<T?> GetAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default);
    Task<T?> PutAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default);
    Task<T?> PatchAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default);
    Task<T?> DeleteAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default);
    Task<ETagResponse<T>> GetWithETagAsync<T>(string path, string? etag, bool useInstanceCredential = false, CancellationToken ct = default);
    void SetAccessToken(string? token);
    void SetInstanceCredential(string? credential);
    string? CurrentAccessToken { get; }
    string? CurrentInstanceCredential { get; }
    Func<Task<bool>>? OnRefreshTokenAsync { get; set; }
}

public sealed class ManagedApiClient : IManagedApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private string? _accessToken;
    private string? _instanceCredential;

    public Func<Task<bool>>? OnRefreshTokenAsync { get; set; }

    public ManagedApiClient(HttpClient httpClient, string baseUrl, TimeSpan? timeout = null)
    {
        _httpClient = httpClient;
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public void SetAccessToken(string? token) => _accessToken = token;
    public void SetInstanceCredential(string? credential) => _instanceCredential = credential;
    public string? CurrentAccessToken => _accessToken;
    public string? CurrentInstanceCredential => _instanceCredential;

    public async Task<T?> PostAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Post, path, body, useInstanceCredential, allowRetry, ct);

    public async Task<T?> GetAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Get, path, null, useInstanceCredential, allowRetry, ct);

    public async Task<T?> PutAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Put, path, body, useInstanceCredential, allowRetry, ct);

    public async Task<T?> PatchAsync<T>(string path, object? body, bool useInstanceCredential = false, bool allowRetry = false, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Patch, path, body, useInstanceCredential, allowRetry, ct);

    public async Task<T?> DeleteAsync<T>(string path, bool useInstanceCredential = false, bool allowRetry = true, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Delete, path, null, useInstanceCredential, allowRetry, ct);

    public async Task<ETagResponse<T>> GetWithETagAsync<T>(string path, string? etag, bool useInstanceCredential = false, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var url = BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(_accessToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        if (useInstanceCredential && !string.IsNullOrEmpty(_instanceCredential))
            request.Headers.TryAddWithoutValidation("X-NetAccel-Instance-Credential", _instanceCredential);

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to {path} timed out.");
        }

        // 401 refresh + retry logic (consistent with SendAsync)
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && !string.IsNullOrEmpty(_accessToken)
            && !IsRefreshEndpoint(path))
        {
            var refreshed = OnRefreshTokenAsync != null && await OnRefreshTokenAsync.Invoke();
            if (refreshed)
            {
                // Rebuild request with new token, preserving If-None-Match
                using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(_accessToken))
                    retryRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                if (useInstanceCredential && !string.IsNullOrEmpty(_instanceCredential))
                    retryRequest.Headers.TryAddWithoutValidation("X-NetAccel-Instance-Credential", _instanceCredential);
                if (!string.IsNullOrEmpty(etag))
                    retryRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
                retryRequest.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString());

                try
                {
                    response = await _httpClient.SendAsync(retryRequest, cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Retry request to {path} timed out.");
                }
            }
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
            return new ETagResponse<T> { IsNotModified = true };

        var data = await ParseResponseAsync<T>(response, path, ct);
        return new ETagResponse<T> { Data = data, IsNotModified = false };
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, bool useInstanceCredential, bool allowRetry, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var url = BuildUrl(path);
        using var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrEmpty(_accessToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        if (useInstanceCredential && !string.IsNullOrEmpty(_instanceCredential))
            request.Headers.TryAddWithoutValidation("X-NetAccel-Instance-Credential", _instanceCredential);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to {path} timed out.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized
            && allowRetry
            && !string.IsNullOrEmpty(_accessToken)
            && !IsRefreshEndpoint(path))
        {
            var refreshed = OnRefreshTokenAsync != null && await OnRefreshTokenAsync.Invoke();
            if (refreshed)
            {
                return await SendAsync<T>(method, path, body, useInstanceCredential, allowRetry: false, ct);
            }
        }

        return await ParseResponseAsync<T>(response, path, ct);
    }

    private static async Task<T?> ParseResponseAsync<T>(HttpResponseMessage response, string path, CancellationToken ct)
    {
        var responseText = await response.Content.ReadAsStringAsync(ct);
        var envelope = JsonSerializer.Deserialize<ManagedApiResponse>(responseText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        if (envelope == null)
            throw new ManagedApiError((int)response.StatusCode, "unknown", "Empty or invalid response.", null);

        if (!response.IsSuccessStatusCode)
        {
            throw new ManagedApiError((int)response.StatusCode, envelope.ErrorCode ?? "unknown", envelope.Message, response.Headers.TryGetValues("X-Request-ID", out var ids) ? ids.FirstOrDefault() : null);
        }

        if (envelope.Data == null || envelope.Data.Value.ValueKind == JsonValueKind.Null)
            return default;

        return JsonSerializer.Deserialize<T>(envelope.Data.Value.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    /// <summary>
    /// Normalizes base URL to just the host part (no /api/v1 suffix).
    /// BuildUrl will add /api/v1 exactly once.
    /// </summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        // If base ends with /api/v1, strip it to avoid double /api/v1
        const string apiSuffix = "/api/v1";
        if (trimmed.EndsWith(apiSuffix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^apiSuffix.Length];
        }
        return trimmed;
    }

    /// <summary>
    /// Builds the final request URL.
    /// Ensures exactly one /api/v1 prefix regardless of what the caller passes.
    /// Supports: /client/login, client/login, /api/v1/client/login, api/v1/client/login
    /// </summary>
    private string BuildUrl(string path)
    {
        var normalizedPath = path.Trim();
        const string apiPrefix = "/api/v1";

        // Strip leading /api/v1 if present
        if (normalizedPath.StartsWith(apiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[apiPrefix.Length..];
        }
        else if (normalizedPath.StartsWith("api/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath[("api/v1".Length)..];
        }

        // Ensure path starts with /
        if (!normalizedPath.StartsWith('/'))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return _baseUrl + apiPrefix + normalizedPath;
    }

    private static bool IsRefreshEndpoint(string path)
    {
        var normalized = path.Trim();
        return normalized.EndsWith("/api/v1/client/refresh", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("api/v1/client/refresh", StringComparison.OrdinalIgnoreCase);
    }
}
