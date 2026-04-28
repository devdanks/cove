using System.Diagnostics;

namespace Cove.PerformanceTests.Infrastructure;

public sealed record PerformanceMeasurement(
    double MeanMs,
    double P95Ms,
    double MinMs,
    double MaxMs,
    int Iterations);

public static class PerformanceProbe
{
    public static async Task<PerformanceMeasurement> MeasureAsync(
        Func<CancellationToken, Task> operation,
        int warmupIterations,
        int measuredIterations,
        CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < warmupIterations; index++)
        {
            await operation(cancellationToken);
        }

        var samples = new double[measuredIterations];
        for (var index = 0; index < measuredIterations; index++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            await operation(cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            samples[index] = elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var mean = samples.Average();
        var p95Index = Math.Min(samples.Length - 1, (int)Math.Ceiling(samples.Length * 0.95) - 1);

        return new PerformanceMeasurement(
            MeanMs: Math.Round(mean, 2),
            P95Ms: Math.Round(samples[p95Index], 2),
            MinMs: Math.Round(samples[0], 2),
            MaxMs: Math.Round(samples[^1], 2),
            Iterations: measuredIterations);
    }
}