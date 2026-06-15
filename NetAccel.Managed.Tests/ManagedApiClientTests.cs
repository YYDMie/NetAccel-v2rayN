using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Dto;
using Xunit;

namespace NetAccel.Managed.Tests;

public class ManagedApiClientTests
{
    private static HttpClient CreateFakeClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler));
    }

    [Fact]
    public async Task PostAsync_ShouldReturnDeserializedData()
    {
        var expected = new LoginResponse { AccessToken = "at_123", RefreshToken = "rt_456" };
        var envelope = new ManagedApiResponse<LoginResponse> { Code = 200, Message = "success", ErrorCode = "", Data = expected };
        var json = JsonSerializer.Serialize(envelope);

        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("POST", req.Method.Method);
            Assert.Equal("https://api.example.com/api/v1/client/login", req.RequestUri?.ToString());
            Assert.NotNull(req.Headers.GetValues("X-Request-ID").FirstOrDefault());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com", client);
        var result = await api.PostAsync<LoginResponse>("/client/login", new LoginRequest(), ct: default);

        Assert.Equal("at_123", result.AccessToken);
        Assert.Equal("rt_456", result.RefreshToken);
    }

    [Fact]
    public async Task PostAsync_ShouldThrowManagedApiException_WithErrorCode()
    {
        var envelope = new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = ManagedErrorCode.InvalidCredentials, Data = null };
        var json = JsonSerializer.Serialize(envelope);

        var client = CreateFakeClient((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

        using var api = new ManagedApiClient("https://api.example.com", client);
        var ex = await Assert.ThrowsAsync<ManagedApiException>(() => api.PostAsync<object>("/client/login", new LoginRequest(), ct: default));

        Assert.Equal(401, ex.HttpStatusCode);
        Assert.Equal(ManagedErrorCode.InvalidCredentials, ex.ErrorCode);
        Assert.False(string.IsNullOrEmpty(ex.RequestId));
    }

    [Fact]
    public async Task PostAsync_ShouldThrowNetworkTimeout_WhenCancelledByTimeout()
    {
        var client = CreateFakeClient(async (req, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var api = new ManagedApiClient("https://api.example.com", client, timeout: TimeSpan.FromMilliseconds(50));
        var ex = await Assert.ThrowsAsync<ManagedNetworkTimeoutException>(() => api.PostAsync<object>("/client/login", new LoginRequest(), ct: default));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task PostAsync_ShouldThrowOperationCancelled_WhenCtIsCancelled()
    {
        var client = CreateFakeClient(async (req, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var api = new ManagedApiClient("https://api.example.com", client);
        await Assert.ThrowsAsync<ManagedOperationCancelledException>(() => api.PostAsync<object>("/client/login", new LoginRequest(), ct: cts.Token));
    }

    [Fact]
    public async Task GetAsync_ShouldIncludeBearerAndInstanceHeaders()
    {
        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("Bearer at_123", req.Headers.Authorization?.ToString());
            Assert.Contains("instcred_789", req.Headers.GetValues("X-NetAccel-Instance-Credential"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"policy_revision\":1}}", Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com", client);
        var result = await api.GetAsync<ManagedPolicy>("/client/managed/policy", accessToken: "at_123", instanceCredential: "instcred_789", ct: default);
        Assert.Equal(1, result.PolicyRevision);
    }

    [Fact]
    public async Task SendRawAsync_ShouldReturn304WithoutThrowing()
    {
        var client = CreateFakeClient((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)));

        using var api = new ManagedApiClient("https://api.example.com", client);
        var (status, content, requestId) = await api.SendRawAsync(HttpMethod.Get, "/client/managed/config", null, "at", "ic", ct: default);
        Assert.Equal(HttpStatusCode.NotModified, status);
    }

    [Fact]
    public async Task SendRawAsync_401_ShouldThrowStructuredException()
    {
        var envelope = new ManagedApiResponse
        {
            Code = 401,
            Message = "unauthorized",
            ErrorCode = ManagedErrorCode.InvalidCredentials,
        };
        var client = CreateFakeClient((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"),
        }));

        using var api = new ManagedApiClient("https://api.example.com", client);
        var ex = await Assert.ThrowsAsync<ManagedApiException>(() =>
            api.SendRawAsync(HttpMethod.Get, "/client/managed/config", null, "at", "ic", ct: default));

        Assert.Equal(401, ex.HttpStatusCode);
        Assert.Equal(ManagedErrorCode.InvalidCredentials, ex.ErrorCode);
    }

    [Fact]
    public async Task SendRawAsync_ShouldIncludeExtraHeaders()
    {
        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("\"7\"", req.Headers.IfNoneMatch.First().Tag);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com", client);
        await api.SendRawAsync(HttpMethod.Get, "/client/managed/config", null, "at", "ic", extraHeaders: new Dictionary<string, string> { ["If-None-Match"] = "\"7\"" }, ct: default);
    }

    [Fact]
    public async Task GetAsync_ShouldIncludeExtraHeaders()
    {
        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("\"42\"", req.Headers.IfNoneMatch.First().Tag);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{\"policy_revision\":1}}", Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com", client);
        var result = await api.GetAsync<ManagedPolicy>("/client/managed/policy", accessToken: "at", instanceCredential: "ic", extraHeaders: new Dictionary<string, string> { ["If-None-Match"] = "\"42\"" }, ct: default);
        Assert.Equal(1, result.PolicyRevision);
    }

    [Fact]
    public async Task Constructor_BaseUrlWithoutApiV1_AppendsApiV1()
    {
        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("https://api.example.com/api/v1/client/login", req.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com", client);
        await api.PostAsync<object>("/client/login", new { }, ct: default);
    }

    [Fact]
    public async Task Constructor_BaseUrlWithApiV1_DoesNotDoublePrefix()
    {
        var client = CreateFakeClient((req, ct) =>
        {
            Assert.Equal("https://api.example.com/api/v1/client/login", req.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"ok\",\"error_code\":\"\",\"data\":{}}", Encoding.UTF8, "application/json"),
            });
        });

        using var api = new ManagedApiClient("https://api.example.com/api/v1", client);
        await api.PostAsync<object>("/client/login", new { }, ct: default);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
