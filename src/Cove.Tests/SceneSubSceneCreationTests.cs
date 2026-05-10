using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Data;
using Cove.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cove.Tests;

public class SceneSubSceneCreationTests
{
    [Fact]
    public async Task ScenesController_Create_AllowsNestedSubScenesUsingRelativeClipOffsets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(new CovePrincipal
        {
            UserId = 1,
            Username = "test-user",
            Kind = PrincipalKind.User,
            Permissions = new HashSet<string> { "*" },
            Roles = new HashSet<string>(),
        });

        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new CoveContext(options, principalAccessor);
        await context.Database.EnsureCreatedAsync();

        var sourceScene = new Scene
        {
            Title = "Source Scene",
            MaxDuration = 120,
        };
        var childScene = new Scene
        {
            Title = "Child Scene",
            ParentScene = sourceScene,
            ClipStartSec = 30,
            ClipEndSec = 60,
            MaxDuration = 30,
        };

        context.Scenes.AddRange(sourceScene, childScene);
        await context.SaveChangesAsync();

        var controller = new ScenesController(
            new SceneRepository(context),
            context,
            null!,
            null!,
            null!,
            new MemoryCache(new MemoryCacheOptions()),
            null!,
            null!,
            null!,
            new NoOpUserEngagementService(),
            new CustomFieldService(context),
            null,
            principalAccessor);

        var createResult = await controller.Create(new SceneCreateDto(
            Title: "Nested Scene",
            Code: null,
            Details: null,
            Director: null,
            Date: null,
            Rating: null,
            Organized: false,
            StudioId: null,
            Captions: null,
            InteractiveSpeed: null,
            Urls: null,
            TagIds: null,
            PerformerIds: null,
            GalleryIds: null,
            Groups: null,
            CustomFields: null,
            ParentSceneId: childScene.Id,
            ClipStartSec: 5,
            ClipEndSec: 10), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var createdDto = Assert.IsType<SceneDto>(created.Value);

        Assert.Equal(sourceScene.Id, createdDto.ParentSceneId);
        Assert.Equal(35, createdDto.ClipStartSec);
        Assert.Equal(40, createdDto.ClipEndSec);

        var storedScene = await context.Scenes.SingleAsync(scene => scene.Id == createdDto.Id);
        Assert.Equal(sourceScene.Id, storedScene.ParentSceneId);
        Assert.Equal(35, storedScene.ClipStartSec);
        Assert.Equal(40, storedScene.ClipEndSec);
    }
}