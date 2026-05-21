namespace Cove.Core.Media;

public sealed class BoundingBoxKeyframeSelectionOptions
{
    public double IoUThreshold { get; init; } = 0.86;

    public int MaxKeyframes { get; init; } = 18;

    public double MaxGapSeconds { get; init; } = 2.5;

    public BoundingBoxKeyframeSelectionOptions Normalize()
        => new()
        {
            IoUThreshold = Math.Clamp(IoUThreshold, 0.0, 1.0),
            MaxKeyframes = Math.Clamp(MaxKeyframes, 1, 60),
            MaxGapSeconds = Math.Clamp(MaxGapSeconds, 0.0, 60.0),
        };
}

public readonly record struct BoundingBoxKeyframe(
    double X1,
    double Y1,
    double X2,
    double Y2,
    double? TimeSeconds,
    int Order,
    string? Key = null)
{
    public double Area => Math.Max(0.0, Math.Abs(X2 - X1)) * Math.Max(0.0, Math.Abs(Y2 - Y1));
}

public static class BoundingBoxKeyframeSelector
{
    public static IReadOnlyList<TSample> Select<TSample>(
        IReadOnlyList<TSample> samples,
        TSample bestSample,
        Func<TSample, BoundingBoxKeyframe> project,
        BoundingBoxKeyframeSelectionOptions options)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var normalizedOptions = options.Normalize();
        var orderedSamples = samples
            .OrderBy(sample => project(sample).TimeSeconds ?? double.MinValue)
            .ThenBy(sample => project(sample).Order)
            .ToArray();

        var selected = new List<TSample> { orderedSamples[0] };
        var lastMaterialSample = orderedSamples[0];

        foreach (var sample in orderedSamples.Skip(1))
        {
            var previousFrame = project(lastMaterialSample);
            var currentFrame = project(sample);
            if (!HasMaterialDetectionChange(previousFrame, currentFrame, normalizedOptions.IoUThreshold)
                && !ExceedsDetectionKeyframeGap(previousFrame, currentFrame, normalizedOptions.MaxGapSeconds))
            {
                continue;
            }

            selected.Add(sample);
            lastMaterialSample = sample;
        }

        AddIfMateriallyDistinct(selected, bestSample, project, normalizedOptions.IoUThreshold);
        AddIfMateriallyDistinct(selected, orderedSamples[^1], project, normalizedOptions.IoUThreshold);

        return CapKeyframes(selected, bestSample, project, normalizedOptions.MaxKeyframes);
    }

    private static void AddIfMateriallyDistinct<TSample>(
        List<TSample> selected,
        TSample sample,
        Func<TSample, BoundingBoxKeyframe> project,
        double iouThreshold)
    {
        var sampleFrame = project(sample);
        if (selected.Any(existing => IsSameSample(project(existing), sampleFrame)))
        {
            return;
        }

        if (selected.Any(existing => !HasMaterialDetectionChange(project(existing), sampleFrame, iouThreshold)))
        {
            return;
        }

        selected.Add(sample);
    }

    private static IReadOnlyList<TSample> CapKeyframes<TSample>(
        List<TSample> selected,
        TSample bestSample,
        Func<TSample, BoundingBoxKeyframe> project,
        int maxCount)
    {
        var distinct = selected
            .DistinctBy(sample => GetSampleIdentity(project(sample)))
            .OrderBy(sample => project(sample).TimeSeconds ?? double.MinValue)
            .ThenBy(sample => project(sample).Order)
            .ToArray();
        if (distinct.Length <= maxCount)
        {
            return distinct;
        }

        var required = new List<TSample>();
        AddRequired(required, distinct[0], project);
        AddRequired(required, bestSample, project);
        AddRequired(required, distinct[^1], project);

        var remainingSlots = Math.Max(0, maxCount - required.Count);
        var optional = distinct
            .Where(sample => !required.Any(requiredSample => IsSameSample(project(requiredSample), project(sample))))
            .ToArray();

        if (remainingSlots > 0 && optional.Length > 0)
        {
            if (remainingSlots == 1)
            {
                AddRequired(required, optional[optional.Length / 2], project);
            }
            else
            {
                for (var index = 0; index < remainingSlots; index++)
                {
                    var optionalIndex = (int)Math.Round(index * (optional.Length - 1) / (double)(remainingSlots - 1), MidpointRounding.AwayFromZero);
                    AddRequired(required, optional[optionalIndex], project);
                }
            }
        }

        return required
            .DistinctBy(sample => GetSampleIdentity(project(sample)))
            .OrderBy(sample => project(sample).TimeSeconds ?? double.MinValue)
            .ThenBy(sample => project(sample).Order)
            .ToArray();
    }

    private static void AddRequired<TSample>(List<TSample> required, TSample sample, Func<TSample, BoundingBoxKeyframe> project)
    {
        var frame = project(sample);
        if (!required.Any(existing => IsSameSample(project(existing), frame)))
        {
            required.Add(sample);
        }
    }

    private static bool HasMaterialDetectionChange(BoundingBoxKeyframe previous, BoundingBoxKeyframe current, double iouThreshold)
    {
        if (previous.Area <= 0.0 || current.Area <= 0.0)
        {
            return true;
        }

        return ComputeIoU(previous, current) < iouThreshold;
    }

    private static bool ExceedsDetectionKeyframeGap(BoundingBoxKeyframe previous, BoundingBoxKeyframe current, double maxGapSeconds)
    {
        if (maxGapSeconds <= 0.0 || !previous.TimeSeconds.HasValue || !current.TimeSeconds.HasValue)
        {
            return false;
        }

        return current.TimeSeconds.Value - previous.TimeSeconds.Value >= maxGapSeconds;
    }

    private static bool IsSameSample(BoundingBoxKeyframe left, BoundingBoxKeyframe right)
    {
        if (!string.IsNullOrWhiteSpace(left.Key) || !string.IsNullOrWhiteSpace(right.Key))
        {
            return string.Equals(left.Key, right.Key, StringComparison.Ordinal);
        }

        return left.Order == right.Order
            && Nullable.Equals(left.TimeSeconds, right.TimeSeconds)
            && BoundingBoxesMatch(left, right);
    }

    private static string GetSampleIdentity(BoundingBoxKeyframe sample)
        => !string.IsNullOrWhiteSpace(sample.Key)
            ? sample.Key
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{sample.Order}\u001F{sample.TimeSeconds:R}\u001F{sample.X1:R},{sample.Y1:R},{sample.X2:R},{sample.Y2:R}");

    private static bool BoundingBoxesMatch(BoundingBoxKeyframe left, BoundingBoxKeyframe right)
        => Math.Abs(left.X1 - right.X1) <= 0.0001
           && Math.Abs(left.Y1 - right.Y1) <= 0.0001
           && Math.Abs(left.X2 - right.X2) <= 0.0001
           && Math.Abs(left.Y2 - right.Y2) <= 0.0001;

    private static double ComputeIoU(BoundingBoxKeyframe left, BoundingBoxKeyframe right)
    {
        var x1 = Math.Max(Math.Min(left.X1, left.X2), Math.Min(right.X1, right.X2));
        var y1 = Math.Max(Math.Min(left.Y1, left.Y2), Math.Min(right.Y1, right.Y2));
        var x2 = Math.Min(Math.Max(left.X1, left.X2), Math.Max(right.X1, right.X2));
        var y2 = Math.Min(Math.Max(left.Y1, left.Y2), Math.Max(right.Y1, right.Y2));
        var width = Math.Max(0.0, x2 - x1);
        var height = Math.Max(0.0, y2 - y1);
        var intersection = width * height;
        var union = left.Area + right.Area - intersection;
        return union <= 0.0 ? 0.0 : intersection / union;
    }
}