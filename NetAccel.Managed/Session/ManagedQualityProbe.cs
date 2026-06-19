using System.Diagnostics;
using System.Net.Sockets;

namespace NetAccel.Managed.Session;

public interface IManagedQualityProbe
{
    Task<ManagedQualityMeasurement> MeasureAsync(ManagedQualityTarget target, CancellationToken ct = default);
}

public sealed class ManagedQualityWindow
{
    private readonly int _capacity;
    private readonly Queue<ManagedQualityMeasurement> _samples = new();

    public ManagedQualityWindow(int capacity = 10)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public ManagedQualityMeasurement Add(ManagedQualityMeasurement sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > _capacity)
        {
            _samples.Dequeue();
        }

        var latencies = _samples
            .Select(item => item.LatencyMs)
            .Where(value => value is >= 0 && double.IsFinite(value.Value))
            .Select(value => value!.Value)
            .ToArray();
        double? jitter = null;
        if (latencies.Length >= 2)
        {
            jitter = latencies
                .Zip(latencies.Skip(1), (left, right) => Math.Abs(right - left))
                .Average();
        }

        var lossSamples = _samples
            .Select(item => item.LossRate)
            .Where(value => value is >= 0 and <= 1)
            .Select(value => value!.Value)
            .ToArray();

        return sample with
        {
            JitterMs = jitter,
            LossRate = lossSamples.Length == 0 ? null : lossSamples.Average(),
        };
    }
}

public sealed class TcpManagedQualityProbe : IManagedQualityProbe
{
    private readonly TimeSpan _timeout;
    private readonly Func<DateTimeOffset> _clock;

    public TcpManagedQualityProbe(TimeSpan? timeout = null, Func<DateTimeOffset>? clock = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ManagedQualityMeasurement> MeasureAsync(
        ManagedQualityTarget target,
        CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(target.Host, target.Port, timeout.Token);
            stopwatch.Stop();
            return new ManagedQualityMeasurement
            {
                LatencyMs = stopwatch.Elapsed.TotalMilliseconds,
                LossRate = 0,
                Source = "tcp_connect",
                Accuracy = "measured",
                SampledAt = _clock(),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FailedMeasurement();
        }
        catch (SocketException)
        {
            return FailedMeasurement();
        }

        ManagedQualityMeasurement FailedMeasurement()
            => new()
            {
                LossRate = 1,
                Source = "tcp_connect",
                Accuracy = "measured",
                SampledAt = _clock(),
            };
    }
}
