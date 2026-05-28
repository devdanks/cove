using Cove.Api.Services;
using Cove.Core.Entities;

namespace Cove.Tests;

public class ScanServiceTests
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg" };
    private static readonly HashSet<string> GalleryExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".epub" };

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

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsTrueForGalleryArchiveWhenImagesAreExcluded()
    {
        Assert.True(ScanService.IsMediaTypeExcludedByScanTarget(
            ".zip",
            excludeVideo: false,
            excludeImage: true,
            excludeAudio: false,
            excludeText: false,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsTrueForTextsWhenTextsAreExcluded()
    {
        Assert.True(ScanService.IsMediaTypeExcludedByScanTarget(
            ".epub",
            excludeVideo: false,
            excludeImage: false,
            excludeAudio: false,
            excludeText: true,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }

    [Fact]
    public void IsMediaTypeExcludedByScanTarget_ReturnsFalseForAllowedMediaTypes()
    {
        Assert.False(ScanService.IsMediaTypeExcludedByScanTarget(
            ".zip",
            excludeVideo: false,
            excludeImage: false,
            excludeAudio: false,
            excludeText: false,
            VideoExtensions,
            ImageExtensions,
            GalleryExtensions,
            AudioExtensions,
            TextExtensions));
    }
}