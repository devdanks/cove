using Cove.Core.Entities;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public interface ISceneCoverService
{
    Task<bool> TryApplyRemoteCoverAsync(Scene scene, string? imageUrl, CancellationToken ct = default);
}

public sealed class SceneCoverService(IBlobService blobService, IHttpClientFactory httpClientFactory, ILogger<SceneCoverService> logger) : ISceneCoverService
{
    public async Task<bool> TryApplyRemoteCoverAsync(Scene scene, string? imageUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (string.IsNullOrWhiteSpace(imageUrl))
            return false;

        try
        {
            var client = httpClientFactory.CreateClient("scraper");
            using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return false;

            var detectedContentType = DetectImageContentType(bytes);
            var declaredContentType = response.Content.Headers.ContentType?.MediaType;
            var contentType = detectedContentType
                ?? (declaredContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                    ? declaredContentType
                    : null);

            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            await using var stream = new MemoryStream(bytes);
            var newBlobId = await blobService.StoreBlobAsync(stream, contentType, ct);
            var previousBlobId = scene.ImageBlobId;

            scene.ImageBlobId = newBlobId;

            if (!string.IsNullOrWhiteSpace(previousBlobId) && !string.Equals(previousBlobId, newBlobId, StringComparison.Ordinal))
            {
                await blobService.DeleteBlobAsync(previousBlobId, ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply remote cover for scene {SceneId}", scene.Id);
            return false;
        }
    }

    private static string? DetectImageContentType(byte[] data)
    {
        if (data.Length < 4)
            return null;

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";

        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return "image/png";

        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            return "image/gif";

        if (data.Length >= 12
            && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return "image/webp";

        if (data.Length >= 12
            && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70
            && data[8] == 0x61 && data[9] == 0x76 && data[10] == 0x69 && data[11] == 0x66)
            return "image/avif";

        if (data[0] == 0x42 && data[1] == 0x4D)
            return "image/bmp";

        if (data.Length >= 4)
        {
            var littleEndianTiff = data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00;
            var bigEndianTiff = data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A;
            if (littleEndianTiff || bigEndianTiff)
                return "image/tiff";
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
            return "image/jxl";

        if (data.Length >= 8
            && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C
            && data[4] == 0x4A && data[5] == 0x58 && data[6] == 0x4C && data[7] == 0x20)
            return "image/jxl";

        if (LooksLikeSvg(data))
            return "image/svg+xml";

        return null;
    }

    private static bool LooksLikeSvg(byte[] data)
    {
        var head = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 256));
        var trimmed = head.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}