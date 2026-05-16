using Cove.Api.Services;
using Cove.Core.Entities;

namespace Cove.Tests;

public class ScanServiceTests
{
    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsTrueWhenDurationIsMissing()
    {
        var videoFile = new VideoFile
        {
            Width = 1920,
            Height = 1080,
            Duration = 0,
        };

        Assert.True(ScanService.NeedsVideoMetadataProbe(videoFile));
    }

    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsTrueWhenDimensionsAreMissing()
    {
        var videoFile = new VideoFile
        {
            Width = 0,
            Height = 1080,
            Duration = 307.9,
        };

        Assert.True(ScanService.NeedsVideoMetadataProbe(videoFile));
    }

    [Fact]
    public void NeedsVideoMetadataProbe_ReturnsFalseWhenCoreVideoMetricsExist()
    {
        var videoFile = new VideoFile
        {
            Width = 1920,
            Height = 1080,
            Duration = 307.9,
        };

        Assert.False(ScanService.NeedsVideoMetadataProbe(videoFile));
    }
}