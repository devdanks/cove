namespace Cove.Core.Services;

public static class IntervalAlgebra
{
    public readonly record struct Interval(double Start, double End);

    public static List<Interval> Union(IEnumerable<Interval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        var normalized = Normalize(intervals);
        return MergeSorted(normalized, 0);
    }

    public static List<Interval> Intersection(IReadOnlyList<List<Interval>> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        if (sets.Count == 0)
            return [];

        var current = Union(sets[0]);
        for (var index = 1; index < sets.Count; index++)
        {
            current = IntersectTwo(current, Union(sets[index]));
            if (current.Count == 0)
                return current;
        }

        return current;
    }

    public static List<Interval> Difference(IReadOnlyList<Interval> minuend, IReadOnlyList<Interval> subtrahend)
    {
        ArgumentNullException.ThrowIfNull(minuend);
        ArgumentNullException.ThrowIfNull(subtrahend);

        var left = Union(minuend);
        var right = Union(subtrahend);
        if (left.Count == 0 || right.Count == 0)
            return left;

        var results = new List<Interval>();
        var rightIndex = 0;

        foreach (var interval in left)
        {
            var cursor = interval.Start;

            while (rightIndex < right.Count && right[rightIndex].End <= cursor)
                rightIndex++;

            var scanIndex = rightIndex;
            while (scanIndex < right.Count)
            {
                var other = right[scanIndex];
                if (other.Start >= interval.End)
                    break;

                if (other.Start > cursor)
                    results.Add(new Interval(cursor, Math.Min(other.Start, interval.End)));

                cursor = Math.Max(cursor, other.End);
                if (cursor >= interval.End)
                    break;

                scanIndex++;
            }

            if (cursor < interval.End)
                results.Add(new Interval(cursor, interval.End));
        }

        return results;
    }

    public static List<Interval> Merge(IReadOnlyList<Interval> intervals, double gapSec)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        if (gapSec < 0)
            throw new ArgumentOutOfRangeException(nameof(gapSec), "Merge gap cannot be negative.");

        var normalized = Normalize(intervals);
        return MergeSorted(normalized, gapSec);
    }

    public static List<Interval> Filter(IReadOnlyList<Interval> intervals, double minDurationSec)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        if (minDurationSec < 0)
            throw new ArgumentOutOfRangeException(nameof(minDurationSec), "Minimum duration cannot be negative.");

        var results = new List<Interval>(intervals.Count);
        for (var index = 0; index < intervals.Count; index++)
        {
            var interval = intervals[index];
            if (interval.End <= interval.Start)
                continue;

            if ((interval.End - interval.Start) >= minDurationSec)
                results.Add(interval);
        }

        return results;
    }

    private static List<Interval> Normalize(IEnumerable<Interval> intervals)
    {
        var normalized = new List<Interval>();
        foreach (var interval in intervals)
        {
            if (interval.End <= interval.Start)
                continue;

            normalized.Add(interval);
        }

        normalized.Sort(static (left, right) =>
        {
            var startComparison = left.Start.CompareTo(right.Start);
            return startComparison != 0 ? startComparison : left.End.CompareTo(right.End);
        });

        return normalized;
    }

    private static List<Interval> MergeSorted(List<Interval> intervals, double gapSec)
    {
        if (intervals.Count == 0)
            return intervals;

        var results = new List<Interval>(intervals.Count);
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;

        for (var index = 1; index < intervals.Count; index++)
        {
            var next = intervals[index];
            if (next.Start <= currentEnd + gapSec)
            {
                if (next.End > currentEnd)
                    currentEnd = next.End;

                continue;
            }

            results.Add(new Interval(currentStart, currentEnd));
            currentStart = next.Start;
            currentEnd = next.End;
        }

        results.Add(new Interval(currentStart, currentEnd));
        return results;
    }

    private static List<Interval> IntersectTwo(IReadOnlyList<Interval> left, IReadOnlyList<Interval> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return [];

        var results = new List<Interval>();
        var leftIndex = 0;
        var rightIndex = 0;

        while (leftIndex < left.Count && rightIndex < right.Count)
        {
            var a = left[leftIndex];
            var b = right[rightIndex];
            var start = Math.Max(a.Start, b.Start);
            var end = Math.Min(a.End, b.End);

            if (start < end)
                results.Add(new Interval(start, end));

            if (a.End <= b.End)
                leftIndex++;
            else
                rightIndex++;
        }

        return results;
    }
}