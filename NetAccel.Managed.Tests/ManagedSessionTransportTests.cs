using System.Net;
using System.Text;
using System.Text.Json;
using NetAccel.Managed.Api;
using NetAccel.Managed.Dto;
using NetAccel.Managed.Instance;
using NetAccel.Managed.Session;
using Xunit;

namespace NetAccel.Managed.Tests;

public sealed class ManagedSessionTransportTests
{
    [Fact]
    public async Task LifecycleUsesRuntimeRoutesAndOnlyRestrictedInstanceCredential()
    {
        var requests = new List<CapturedRequest>();
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            requests.Add(await CaptureAsync(request));
            var data = request.RequestUri!.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal)
                ? "{}"
                : "{\"id\":\"server-session\",\"state\":\"active\"}";
            return JsonResponse($"{{\"code\":0,\"message\":\"ok\",\"data\":{data}}}");
        }));
        using var api = new ManagedApiClient("https://master.example", http);
        var transport = new ManagedSessionApiTransport(api, new FakeInstanceService());
        var ct = TestContext.Current.CancellationToken;

        var created = await transport.CreateAsync(new ManagedSessionCreateRequest
        {
            IdempotencyKey = "550e8400-e29b-41d4-a716-446655440001",
            InstanceId = "wrong-instance",
            PlanId = 42,
            Mode = "http",
            ClientStartedAt = DateTimeOffset.Parse("2026-06-19T03:00:00Z"),
        }, ct);
        await transport.ActivateAsync(created.Id, ct);
        await transport.HeartbeatAsync(created.Id, new ManagedSessionHeartbeatRequest
        {
            QualityState = "unknown",
            LatestMetrics = ManagedQualityMapper.Map(new ManagedQualityMeasurement
            {
                SampledAt = DateTimeOffset.Parse("2026-06-19T03:00:30Z"),
            }).Metrics,
        }, ct);
        await transport.CloseAsync(created.Id, new ManagedSessionCloseRequest
        {
            Reason = ManagedSessionReason.UserDisconnect,
            Code = ManagedSessionEventCode.Closed,
        }, ct);

        Assert.Equal(
            new[]
            {
                "/api/v1/client/runtime/sessions",
                "/api/v1/client/runtime/sessions/server-session/activate",
                "/api/v1/client/runtime/sessions/server-session/heartbeat",
                "/api/v1/client/runtime/sessions/server-session/close",
            },
            requests.Select(item => item.Path));
        Assert.All(requests, request =>
        {
            Assert.Null(request.Authorization);
            Assert.Equal("instance-secret", request.InstanceCredential);
        });

        using var createBody = JsonDocument.Parse(requests[0].Body);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440002", createBody.RootElement.GetProperty("instance_id").GetString());
        Assert.Equal(42, createBody.RootElement.GetProperty("plan_id").GetInt32());
        Assert.DoesNotContain("instance-secret", requests[0].Body, StringComparison.Ordinal);
    }

    private static async Task<CapturedRequest> CaptureAsync(HttpRequestMessage request)
    {
        var body = request.Content == null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new CapturedRequest(
            request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-NetAccel-Instance-Credential", out var values) ? values.Single() : null,
            body);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed record CapturedRequest(string Path, string? Authorization, string? InstanceCredential, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class FakeInstanceService : IInstanceService
    {
        public Task<string?> GetInstanceIdAsync()
            => Task.FromResult<string?>("550e8400-e29b-41d4-a716-446655440002");

        public Task<string?> GetInstanceCredentialAsync()
            => Task.FromResult<string?>("instance-secret");

        public Task<InstanceResult> RegisterOrRecoverAsync(
            string installationKey,
            string platform,
            string platformVersion,
            string arch,
            string clientVersion,
            Dictionary<string, string> coreVersions,
            ClientCapabilities capabilities,
            string? oldInstanceCredential,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<HeartbeatResult> HeartbeatAsync(
            string clientVersion,
            Dictionary<string, string> coreVersions,
            ClientCapabilities capabilities,
            string? effectiveProfileId,
            bool sessionActive,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InstanceResult> RotateCredentialAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
