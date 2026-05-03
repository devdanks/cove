using Cove.Core.Services;

namespace Cove.Tests;

public class IntervalAlgebraTests
{
    [Fact]
    public void Union_ReturnsEmpty_ForEmptyInput()
    {
        var result = IntervalAlgebra.Union([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Union_MergesOverlappingAndTouchingIntervals()
    {
        var result = IntervalAlgebra.Union(
        [
            new IntervalAlgebra.Interval(5, 7),
            new IntervalAlgebra.Interval(1, 3),
            new IntervalAlgebra.Interval(3, 4),
            new IntervalAlgebra.Interval(2, 6),
        ]);

        Assert.Equal([new IntervalAlgebra.Interval(1, 7)], result);
    }

    [Fact]
    public void Union_DropsZeroLengthIntervals()
    {
        var result = IntervalAlgebra.Union(
        [
            new IntervalAlgebra.Interval(1, 1),
            new IntervalAlgebra.Interval(2, 5),
            new IntervalAlgebra.Interval(7, 7),
        ]);

        Assert.Equal([new IntervalAlgebra.Interval(2, 5)], result);
    }

    [Fact]
    public void Merge_MergesIntervalsWithinConfiguredGap()
    {
        var result = IntervalAlgebra.Merge(
        [
            new IntervalAlgebra.Interval(1, 3),
            new IntervalAlgebra.Interval(4.5, 6),
            new IntervalAlgebra.Interval(8.2, 9),
        ],
        1.5);

        Assert.Equal(
        [
            new IntervalAlgebra.Interval(1, 6),
            new IntervalAlgebra.Interval(8.2, 9),
        ],
        result);
    }

    [Fact]
    public void Merge_ThrowsForNegativeGap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntervalAlgebra.Merge([new IntervalAlgebra.Interval(1, 2)], -0.1));
    }

    [Fact]
    public void Filter_RemovesIntervalsShorterThanMinimumDuration()
    {
        var result = IntervalAlgebra.Filter(
        [
            new IntervalAlgebra.Interval(1, 1.5),
            new IntervalAlgebra.Interval(2, 4.5),
            new IntervalAlgebra.Interval(6, 6),
        ],
        1.0);

        Assert.Equal([new IntervalAlgebra.Interval(2, 4.5)], result);
    }

    [Fact]
    public void Intersection_ReturnsOverlapAcrossAllSets()
    {
        var result = IntervalAlgebra.Intersection(
        [
            [new IntervalAlgebra.Interval(0, 10)],
            [new IntervalAlgebra.Interval(2, 6), new IntervalAlgebra.Interval(8, 12)],
            [new IntervalAlgebra.Interval(1, 4), new IntervalAlgebra.Interval(5, 9)],
        ]);

        Assert.Equal(
        [
            new IntervalAlgebra.Interval(2, 4),
            new IntervalAlgebra.Interval(5, 6),
            new IntervalAlgebra.Interval(8, 9),
        ],
        result);
    }

    [Fact]
    public void Difference_SubtractsMultipleIntervals()
    {
        var result = IntervalAlgebra.Difference(
        [new IntervalAlgebra.Interval(0, 10)],
        [
            new IntervalAlgebra.Interval(1, 3),
            new IntervalAlgebra.Interval(5, 6),
            new IntervalAlgebra.Interval(8, 12),
        ]);

        Assert.Equal(
        [
            new IntervalAlgebra.Interval(0, 1),
            new IntervalAlgebra.Interval(3, 5),
            new IntervalAlgebra.Interval(6, 8),
        ],
        result);
    }

    [Fact]
    public void Difference_ReturnsOriginalIntervals_WhenSubtrahendIsEmpty()
    {
        var result = IntervalAlgebra.Difference(
            [new IntervalAlgebra.Interval(0, 2), new IntervalAlgebra.Interval(3, 5)],
            []);

        Assert.Equal(
        [
            new IntervalAlgebra.Interval(0, 2),
            new IntervalAlgebra.Interval(3, 5),
        ],
        result);
    }
}