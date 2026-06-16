using System.Net;
using NetAccel.Managed.Api;
using NetAccel.Managed.Dtos;

namespace NetAccel.Managed.Tests.Api;

public class ManagedApiClientTests
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static HttpClient CreateFakeHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new FakeMessageHandler(handler));
    }

    // URL normalization tests covering real service layer paths
    [Theory]
    // From "https://host" with various path forms - all should produce the same URL
    [InlineData("https://host", "/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/", "/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/api/v1", "/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/api/v1/", "/client/login", "https://host/api/v1/client/login")]
    // Path without leading slash
    [InlineData("https://host", "client/login", "https://host/api/v1/client/login")]
    // Path already includes /api/v1 - should NOT duplicate
    [InlineData("https://host", "/api/v1/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/", "/api/v1/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/api/v1", "/api/v1/client/login", "https://host/api/v1/client/login")]
    [InlineData("https://host/api/v1/", "/api/v1/client/login", "https://host/api/v1/client/login")]
    // Path with api/v1 but no leading slash
    [InlineData("https://host", "api/v1/client/login", "https://host/api/v1/client/login")]
    // Real service layer paths
    [InlineData("https://host", "/api/v1/client/managed/config", "https://host/api/v1/client/managed/config")]
    [InlineData("https://host/api/v1", "/api/v1/client/managed/config", "https://host/api/v1/client/managed/config")]
    [InlineData("https://host", "/api/v1/client/managed/status", "https://host/api/v1/client/managed/status")]
    [InlineData("https://host", "/api/v1/client/managed/policy", "https://host/api/v1/client/managed/policy")]
    [InlineData("https://host", "/api/v1/client/managed/selection", "https://host/api/v1/client/managed/selection")]
    [InlineData("https://host", "/api/v1/client/managed/config/42/ack", "https://host/api/v1/client/managed/config/42/ack")]
    [InlineData("https://host", "/api/v1/client/instances/register", "https://host/api/v1/client/instances/register")]
    [InlineData("https://host", "/api/v1/client/instances/inst-1/heartbeat", "https://host/api/v1/client/instances/inst-1/heartbeat")]
    [InlineData("https://host", "/api/v1/client/instances/inst-1/credentials/rotate", "https://host/api/v1/client/instances/inst-1/credentials/rotate")]
    [InlineData("https://host", "/api/v1/client/logout", "https://host/api/v1/client/logout")]
    [InlineData("https://host", "/api/v1/client/refresh", "https://host/api/v1/client/refresh")]
    public void NormalizeBaseUrl_ProducesCorrectUrl(string baseUrl, string path, string expected)
    {
        HttpRequestMessage? captured = null;
        var client = CreateFakeHttpClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 200, Message = "ok", ErrorCode = "", Data = null }, _jsonOptions))
            };
        });
        var api = new ManagedApiClient(client, baseUrl);
        _ = api.GetAsync<object>(path);
        captured!.RequestUri!.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task PostAsync_ReturnsDeserializedData()
    {
        var envelope = new ManagedApiResponse
        {
            Code = 200,
            Message = "ok",
            ErrorCode = "",
            Data = JsonSerializer.Deserialize<JsonElement>(@"{""access_token"": ""abc""}", _jsonOptions)
        };
        var client = CreateFakeHttpClient(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("token");

        var result = await api.PostAsync<LoginResponse>("/login", new { username = "u" });
        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("abc");
    }

    [Fact]
    public async Task PostAsync_ThrowsManagedApiError_OnFailure()
    {
        var envelope = new ManagedApiResponse { Code = 401, Message = "bad", ErrorCode = "client_auth_session_revoked" };
        var client = CreateFakeHttpClient(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        var act = async () => await api.PostAsync<object>("/login", null);

        var ex = await act.Should().ThrowAsync<ManagedApiError>();
        ex.Which.ErrorCode.Should().Be("client_auth_session_revoked");
    }

    [Fact]
    public async Task GetAsync_401_RetriesOnce_AfterRefreshSuccess()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 200, Message = "ok", ErrorCode = "", Data = JsonSerializer.Deserialize<JsonElement>(@"{""id"": ""1""}", _jsonOptions) }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var result = await api.GetAsync<ManagedPolicy>("/policy", allowRetry: true);
        callCount.Should().Be(2);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_401_DoesNotRetry_WhenRefreshFails()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(false);

        var act = async () => await api.GetAsync<ManagedPolicy>("/policy", allowRetry: true);
        await act.Should().ThrowAsync<ManagedApiError>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task PostAsync_401_DoesNotAutoRetry()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var act = async () => await api.PostAsync<object>("/login", new { });
        await act.Should().ThrowAsync<ManagedApiError>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetWithETagAsync_304_ReturnsIsNotModified()
    {
        var client = CreateFakeHttpClient(req =>
        {
            req.Headers.Contains("If-None-Match").Should().BeTrue();
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("token");
        api.SetInstanceCredential("inst");

        var result = await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        result.IsNotModified.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetWithETagAsync_200_ReturnsData()
    {
        var envelope = new ManagedApiResponse
        {
            Code = 200,
            Message = "ok",
            ErrorCode = "",
            Data = JsonSerializer.Deserialize<JsonElement>(@"{""schema"": ""managed-envelope/spike-v0""}", _jsonOptions)
        };
        var client = CreateFakeHttpClient(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("token");

        var result = await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", false);
        result.IsNotModified.Should().BeFalse();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWithETagAsync_401_RefreshThenRetry200()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 200, Message = "ok", ErrorCode = "", Data = JsonSerializer.Deserialize<JsonElement>(@"{""schema"": ""managed-envelope/spike-v0""}", _jsonOptions) }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var result = await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        callCount.Should().Be(2);
        result.IsNotModified.Should().BeFalse();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWithETagAsync_401_RefreshThenRetry304()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var result = await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        callCount.Should().Be(2);
        result.IsNotModified.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetWithETagAsync_401_RefreshFails_ThrowsManagedApiError()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(false);

        var act = async () => await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        await act.Should().ThrowAsync<ManagedApiError>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetWithETagAsync_401_RetriesOnlyOnce()
    {
        var callCount = 0;
        var client = CreateFakeHttpClient(_ =>
        {
            callCount++;
            // Both original and retry get 401
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var act = async () => await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        await act.Should().ThrowAsync<ManagedApiError>();
        // Original 401 + retry 401 = 2 calls total
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetWithETagAsync_IfNoneMatch_PreservedAfterRefresh()
    {
        string? capturedEtag = null;
        var callCount = 0;
        var client = CreateFakeHttpClient(req =>
        {
            callCount++;
            if (req.Headers.Contains("If-None-Match"))
            {
                capturedEtag = string.Join(",", req.Headers.GetValues("If-None-Match"));
            }
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 401, Message = "unauthorized", ErrorCode = "client_auth_session_revoked" }, _jsonOptions))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetAccessToken("old-token");
        api.OnRefreshTokenAsync = () => Task.FromResult(true);

        var result = await api.GetWithETagAsync<ManagedEnvelopeSpikeV0>("/config", "42", true);
        callCount.Should().Be(2);
        result.IsNotModified.Should().BeTrue();
        // ETag was preserved in the retry request
        capturedEtag.Should().Be("42");
    }

    [Fact]
    public async Task GetAsync_UsesInstanceCredentialHeader_WhenFlagSet()
    {
        HttpRequestMessage? captured = null;
        var client = CreateFakeHttpClient(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ManagedApiResponse { Code = 200, Message = "ok", ErrorCode = "", Data = null }, _jsonOptions))
            };
        });

        var api = new ManagedApiClient(client, "http://test");
        api.SetInstanceCredential("inst-cred");
        await api.GetAsync<object>("/status", true);

        captured.Should().NotBeNull();
        captured!.Headers.Contains("X-NetAccel-Instance-Credential").Should().BeTrue();
    }

    private class FakeMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public FakeMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
