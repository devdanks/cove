using Cove.Core.DTOs;
using Cove.Core.Interfaces;

namespace Cove.Tests;

internal sealed class NoOpScanService : IScanService
{
    public string StartScan(ScanOperationOptions? options = null) => "test-scan-job";

    public Task<int> ImportDownloadedSceneAsync(string path, int? sceneId, CancellationToken ct = default) => Task.FromResult(sceneId ?? 0);

    public Task<int> ImportDownloadedImageAsync(string path, int? imageId, CancellationToken ct = default) => Task.FromResult(imageId ?? 0);

    public Task<int> ImportDownloadedGalleryAsync(string path, int? galleryId, CancellationToken ct = default) => Task.FromResult(galleryId ?? 0);

    public Task<int> ImportDownloadedAudioAsync(string path, int? audioId, CancellationToken ct = default) => Task.FromResult(audioId ?? 0);

    public Task<int> ImportDownloadedTextAsync(string path, int? textDocumentId, CancellationToken ct = default) => Task.FromResult(textDocumentId ?? 0);
}