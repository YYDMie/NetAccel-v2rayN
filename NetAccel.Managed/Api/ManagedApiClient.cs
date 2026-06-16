using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetAccel.Managed.Dto;

namespace NetAccel.Managed.Api;

/// <summary>
/// Reusable API client for NetAccel managed endpoints.
/// Parses unified envelopes, maps stable error codes, preserves correlation ids.
/// </summary>
public sealed class ManagedApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly Func<string, Task>? _logAsync;
    private readonly Func<DateTimeOffset> _clock;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ManagedApiClient(
        string baseUrl,
        HttpClient httpClient,
        Func<DateTimeOffset>? clock = null,
        Func<string, Task>? logAsync = null,
        TimeSpan? timeout = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _httpClient = httpClient;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logAsync = logAsync;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (url.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        return url + "/api/v1";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<T> PostAsync<T>(
        string path,
        object? body,
        string? accessToken = null,
        string? instanceCredential = null,
        Dictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        return await SendAsync<T>(HttpMethod.Post, path, body, accessToken, instanceCredential, extraHeaders, ct);
    }

    public async Task<T> GetAsync<T>(
        string path,
        string? accessToken = null,
        string? instanceCredential = null,
        Dictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        return await SendAsync<T>(HttpMethod.Get, path, null, accessToken, instanceCredential, extraHeaders, ct);
    }

    public async Task<T> PutAsync<T>(
        string path,
        object? body,
        string? accessToken = null,
        string? instanceCredential = null,
        Dictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        return await SendAsync<T>(HttpMethod.Put, path, body, accessToken, instanceCredential, extraHeaders, ct);
    }

    public async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        string? instanceCredential,
        Dictionary<string, string>? extraHeaders,
        CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var url = $"{_baseUrl}{path}";
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Request-ID", requestId);

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrEmpty(instanceCredential))
        {
            request.Headers.Add("X-NetAccel-Instance-Credential", instanceCredential);
        }

        if (extraHeaders != null)
        {
            foreach (var h in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
        }

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ManagedNetworkTimeoutException($"Request to {path} timed out after {_timeout.TotalSeconds}s");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new ManagedOperationCancelledException($"Request to {path} was cancelled");
        }
        catch (TaskCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ManagedNetworkTimeoutException($"Request to {path} timed out after {_timeout.TotalSeconds}s");
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            // 304 has no body; caller should handle this before calling SendAsync<T>
            throw new ManagedApiException("304 Not Modified", 304, ManagedErrorCode.ManagedConfigNotModified, requestId);
        }

        var content = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var envelope = JsonSerializer.Deserialize<ManagedApiResponse<T>>(content, _jsonOptions);
            if (envelope is not null && envelope.Data is not null)
            {
                return envelope.Data;
            }
            // Some endpoints may return null data; allow default(T)
            return default(T)!;
        }

        // Parse error envelope
        string errorCode = string.Empty;
        string message = $"HTTP {(int)response.StatusCode}";
        try
        {
            var errorEnvelope = JsonSerializer.Deserialize<ManagedApiResponse>(content, _jsonOptions);
            if (errorEnvelope != null)
            {
                errorCode = errorEnvelope.ErrorCode;
                message = errorEnvelope.Message;
            }
        }
        catch { }

        throw new ManagedApiException(
            $"{message} ({path})",
            (int)response.StatusCode,
            errorCode,
            requestId);
    }

    /// <summary>
    /// Send a request and return raw response for cases where 304 needs special handling.
    /// </summary>
    public async Task<(HttpStatusCode Status, string? Content, string RequestId)> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        string? instanceCredential,
        Dictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var url = $"{_baseUrl}{path}";
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Request-ID", requestId);

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrEmpty(instanceCredential))
        {
            request.Headers.Add("X-NetAccel-Instance-Credential", instanceCredential);
        }

        if (extraHeaders != null)
        {
            foreach (var h in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
        }

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, cts.Token);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotModified || response.IsSuccessStatusCode)
            {
                return (response.StatusCode, content, requestId);
            }

            string errorCode = string.Empty;
            string message = $"HTTP {(int)response.StatusCode}";
            try
            {
                var errorEnvelope = JsonSerializer.Deserialize<ManagedApiResponse>(content, _jsonOptions);
                if (errorEnvelope != null)
                {
                    errorCode = errorEnvelope.ErrorCode;
                    message = errorEnvelope.Message;
                }
            }
            catch { }

            throw new ManagedApiException(
                $"{message} ({path})",
                (int)response.StatusCode,
                errorCode,
                requestId);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ManagedNetworkTimeoutException($"Request to {path} timed out after {_timeout.TotalSeconds}s");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new ManagedOperationCancelledException($"Request to {path} was cancelled");
        }
    }

    /// <summary>
    /// Registers a P-256 device public key for the current instance.
    /// </summary>
    public async Task<DeviceKeyRegisterResponse> RegisterDeviceKeyAsync(
        DeviceKeyRegisterRequest request,
        string accessToken,
        string instanceCredential,
        CancellationToken ct = default)
    {
        return await PostAsync<DeviceKeyRegisterResponse>(
            "/client/managed/keys",
            request,
            accessToken: accessToken,
            instanceCredential: instanceCredential,
            ct: ct);
    }

    /// <summary>
    /// Fetches the stable managed-envelope/v1 for the current instance.
    /// </summary>
    public async Task<ManagedEnvelopeV1> GetEnvelopeV1Async(
        string accessToken,
        string instanceCredential,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var extraHeaders = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            extraHeaders["If-None-Match"] = ifNoneMatch;
        }

        return await GetAsync<ManagedEnvelopeV1>(
            "/client/managed/envelope",
            accessToken: accessToken,
            instanceCredential: instanceCredential,
            extraHeaders: extraHeaders,
            ct: ct);
    }
}
