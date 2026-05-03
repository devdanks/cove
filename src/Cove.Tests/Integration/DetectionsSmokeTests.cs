using System.Net.Http.Json;
using System.Text.Json;
using Cove.Core.DTOs;
using Cove.Core.Entities;

namespace Cove.Tests.Integration;

public sealed class ImageDetectionsControllerSmokeTests
{
    [Fact]
    public async Task List_ReturnsOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var imageId = await factory.WithDbContextAsync(async db =>
        {
            var image = new Image { Title = "Detection Image" };
            db.Images.Add(image);
            await db.SaveChangesAsync();

            db.Detections.Add(new Detection
            {
                HostType = DetectionHostType.Image,
                HostId = image.Id,
                Class = "face",
                Score = 0.94f,
                X = 80,
                Y = 120,
                W = 260,
                H = 320,
                FrameWidth = 1200,
                FrameHeight = 1600,
                Extra = JsonDocument.Parse("{\"embedding\":\"face-1\"}"),
                RefKind = "face",
                RefId = 21,
                GroupKey = "image-track-1",
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-image-1",
            });
            await db.SaveChangesAsync();
            return image.Id;
        });

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/images/{imageId}/detections");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadApiJsonAsync<List<DetectionDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload);
    }
}

public sealed class SceneDetectionsControllerSmokeTests
{
    [Fact]
    public async Task List_ReturnsOk()
    {
        using var factory = new CoveWebApplicationFactory();
        await factory.ResetDatabaseAsync();

        var sceneId = await factory.WithDbContextAsync(async db =>
        {
            var scene = new Scene { Title = "Detection Scene" };
            db.Scenes.Add(scene);
            await db.SaveChangesAsync();

            db.Detections.Add(new Detection
            {
                HostType = DetectionHostType.Scene,
                HostId = scene.Id,
                Class = "face",
                Score = 0.88f,
                X = 100,
                Y = 120,
                W = 220,
                H = 260,
                FrameWidth = 1920,
                FrameHeight = 1080,
                ObservedAtSec = 42.0,
                Extra = JsonDocument.Parse("{\"landmarks\":[1,2,3]}"),
                RefKind = "face",
                RefId = 12,
                GroupKey = "track-1",
                SourceKey = "ext:ai.faces",
                SourceRunId = "run-2",
            });
            await db.SaveChangesAsync();
            return scene.Id;
        });

        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/scenes/{sceneId}/detections");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadApiJsonAsync<List<DetectionDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload);
    }
}