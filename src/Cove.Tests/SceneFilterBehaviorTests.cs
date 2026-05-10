using Cove.Api.Controllers;
using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.DTOs;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Cove.Tests;

public class SceneFilterBehaviorTests
{
    [Fact]
    public async Task PathCriterion_Equals_UsesFullNormalizedPath()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("match", folderPath: @"C:\library\matching", basename: "clip.mp4"),
            CreateSceneWithFile("same-name-other-folder", folderPath: @"C:\library\other", basename: "clip.mp4"));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            PathCriterion = new StringCriterion
            {
                Value = @"C:\library\matching\clip.mp4",
                Modifier = CriterionModifier.Equals,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["match"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task AudioCodecCriterion_HandlesRegexAndNullModifiers()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("aac-scene", audioCodec: "AAC"),
            CreateSceneWithFile("mp3-scene", audioCodec: "MP3"),
            CreateSceneWithFile("missing-audio", audioCodec: ""));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);

        var (notRegexItems, notRegexCount) = await repository.FindAsync(
            new SceneFilter
            {
                AudioCodecCriterion = new StringCriterion
                {
                    Value = "^aa",
                    Modifier = CriterionModifier.NotMatchesRegex,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        var (nullItems, nullCount) = await repository.FindAsync(
            new SceneFilter
            {
                AudioCodecCriterion = new StringCriterion
                {
                    Value = string.Empty,
                    Modifier = CriterionModifier.IsNull,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        var (notNullItems, notNullCount) = await repository.FindAsync(
            new SceneFilter
            {
                AudioCodecCriterion = new StringCriterion
                {
                    Value = string.Empty,
                    Modifier = CriterionModifier.NotNull,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(2, notRegexCount);
        Assert.Equal(["missing-audio", "mp3-scene"], notRegexItems.Select(scene => scene.Title ?? string.Empty).OrderBy(title => title).ToArray());
        Assert.Equal(1, nullCount);
        Assert.Equal(["missing-audio"], nullItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(2, notNullCount);
        Assert.Equal(["aac-scene", "mp3-scene"], notNullItems.Select(scene => scene.Title ?? string.Empty).OrderBy(title => title).ToArray());
    }

    [Fact]
    public async Task BitrateInterval_GreaterThan_UsesSceneFileBitrate()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("high-bitrate", bitRate: 2_500_000),
            CreateSceneWithFile("low-bitrate", bitRate: 500_000),
            new Scene { Title = "no-file" });
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            BitrateInterval = new IntCriterion
            {
                Value = 1000,
                Modifier = CriterionModifier.GreaterThan,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["high-bitrate"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task DirectorCriterion_NotMatchesRegex_UsesRegexSemantics()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("jane-scene", director: "Jane Smith"),
            CreateSceneWithFile("john-scene", director: "John Doe"));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            DirectorCriterion = new StringCriterion
            {
                Value = "^Jane",
                Modifier = CriterionModifier.NotMatchesRegex,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["john-scene"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task PerformerAgeCriterion_Equals_UsesAgeAtSceneDate()
    {
        await using var context = CreateContext();
        var performer = CreatePerformer("Boundary Performer", new DateOnly(2006, 1, 15));

        context.Scenes.AddRange(
            CreateSceneWithFile("before-birthday", sceneDate: new DateOnly(2024, 1, 10), performer: performer),
            CreateSceneWithFile("after-birthday", sceneDate: new DateOnly(2024, 1, 20), performer: performer));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            PerformerAgeCriterion = new IntCriterion
            {
                Value = 18,
                Modifier = CriterionModifier.Equals,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["after-birthday"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task PerformerTagsCriterion_Includes_MatchesScenesByPerformerOccurrenceTag()
    {
        await using var context = CreateContext();
        var tag = new Tag { Name = "Featured" };
        var taggedScene = CreateSceneWithFile("tagged-performer-scene", performer: CreatePerformer("Tagged", new DateOnly(2000, 1, 1)));
        var untaggedScene = CreateSceneWithFile("untagged-performer-scene", performer: CreatePerformer("Untagged", new DateOnly(2000, 1, 1)));

        context.Tags.Add(tag);
        context.Scenes.AddRange(taggedScene, untaggedScene);
        await context.SaveChangesAsync();

        context.TagApplications.Add(new TagApplication
        {
            HostType = AffinityHostType.Scene,
            HostId = taggedScene.Id,
            ContextType = "performer",
            ContextId = taggedScene.ScenePerformers.Single().Performer!.Id,
            TagId = tag.Id,
            SourceKey = "test",
        });
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            PerformerTagsCriterion = new MultiIdCriterion
            {
                Value = [tag.Id],
                Modifier = CriterionModifier.Includes,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["tagged-performer-scene"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task PerformerTagsCriterion_WithPerformerCriterion_MatchesSamePerformerOccurrence()
    {
        await using var context = CreateContext();
        var tag = new Tag { Name = "Occurrence Tag" };
        var targetPerformer = CreatePerformer("Target", new DateOnly(2000, 1, 1));
        var otherPerformer = CreatePerformer("Other", new DateOnly(2000, 1, 1));
        var targetTaggedScene = CreateSceneWithFile("target-tagged", performer: targetPerformer);
        var wrongPerformerTaggedScene = CreateSceneWithFile("wrong-performer-tagged", performer: targetPerformer);
        wrongPerformerTaggedScene.ScenePerformers.Add(new ScenePerformer { Performer = otherPerformer });

        context.Tags.Add(tag);
        context.Scenes.AddRange(targetTaggedScene, wrongPerformerTaggedScene);
        await context.SaveChangesAsync();

        context.TagApplications.AddRange(
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = targetTaggedScene.Id,
                ContextType = "performer",
                ContextId = targetPerformer.Id,
                TagId = tag.Id,
                SourceKey = "test",
            },
            new TagApplication
            {
                HostType = AffinityHostType.Scene,
                HostId = wrongPerformerTaggedScene.Id,
                ContextType = "performer",
                ContextId = otherPerformer.Id,
                TagId = tag.Id,
                SourceKey = "test",
            });
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            PerformersCriterion = new MultiIdCriterion
            {
                Value = [targetPerformer.Id],
                Modifier = CriterionModifier.Includes,
            },
            PerformerTagsCriterion = new MultiIdCriterion
            {
                Value = [tag.Id],
                Modifier = CriterionModifier.Includes,
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["target-tagged"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task TagDurationCriterion_AppliesAllClauses()
    {
        await using var context = CreateContext();
        var shortTag = new Tag { Name = "Short" };
        var percentTag = new Tag { Name = "Percent" };
        var matchingScene = CreateSceneWithFile("matching-duration");
        var longScene = CreateSceneWithFile("too-long");
        var lowPercentScene = CreateSceneWithFile("low-percent");

        context.Tags.AddRange(shortTag, percentTag);
        context.Scenes.AddRange(matchingScene, longScene, lowPercentScene);
        await context.SaveChangesAsync();

        context.TagApplications.AddRange(
            CreateDurationApplication(matchingScene.Id, shortTag.Id, totalDurationSec: 20, hostDurationSec: 100),
            CreateDurationApplication(matchingScene.Id, percentTag.Id, totalDurationSec: 20, hostDurationSec: 100),
            CreateDurationApplication(longScene.Id, shortTag.Id, totalDurationSec: 40, hostDurationSec: 100),
            CreateDurationApplication(longScene.Id, percentTag.Id, totalDurationSec: 20, hostDurationSec: 100),
            CreateDurationApplication(lowPercentScene.Id, shortTag.Id, totalDurationSec: 20, hostDurationSec: 100),
            CreateDurationApplication(lowPercentScene.Id, percentTag.Id, totalDurationSec: 5, hostDurationSec: 100));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            TagDurationCriterion = new TagDurationCriterion
            {
                Clauses =
                [
                    new TagDurationClause { TagId = shortTag.Id, Modifier = CriterionModifier.LessThan, Unit = "seconds", Value = 30 },
                    new TagDurationClause { TagId = percentTag.Id, Modifier = CriterionModifier.GreaterThan, Unit = "percent", Value = 10 },
                ],
            },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, totalCount);
        Assert.Equal(["matching-duration"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task HashAndChecksumCriteria_FilterSceneFingerprints()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile(
                "matching-hashes",
                fingerprints:
                [
                    new FileFingerprint { Type = "oshash", Value = "osh-match" },
                    new FileFingerprint { Type = "md5", Value = "md5-match" },
                ]),
            CreateSceneWithFile(
                "other-hashes",
                fingerprints:
                [
                    new FileFingerprint { Type = "oshash", Value = "osh-other" },
                    new FileFingerprint { Type = "md5", Value = "md5-other" },
                ]));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);

        var (hashItems, hashCount) = await repository.FindAsync(
            new SceneFilter
            {
                HashCriterion = new StringCriterion
                {
                    Value = "osh-match",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        var (checksumItems, checksumCount) = await repository.FindAsync(
            new SceneFilter
            {
                ChecksumCriterion = new StringCriterion
                {
                    Value = "md5-match",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, hashCount);
        Assert.Equal(["matching-hashes"], hashItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(1, checksumCount);
        Assert.Equal(["matching-hashes"], checksumItems.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task FingerprintCriterion_FiltersScenesBySelectedAlgorithm()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile(
                "matching-fingerprint-types",
                fingerprints:
                [
                    new FileFingerprint { Type = "oshash", Value = "osh-match" },
                    new FileFingerprint { Type = "md5", Value = "md5-match" },
                    new FileFingerprint { Type = "phash", Value = "phash-match" },
                ]),
            CreateSceneWithFile(
                "other-fingerprint-types",
                fingerprints:
                [
                    new FileFingerprint { Type = "oshash", Value = "osh-other" },
                    new FileFingerprint { Type = "md5", Value = "md5-other" },
                    new FileFingerprint { Type = "phash", Value = "phash-other" },
                ]));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);

        var (oshashItems, oshashCount) = await repository.FindAsync(
            new SceneFilter
            {
                FingerprintCriterion = new FingerprintCriterion
                {
                    Type = "oshash",
                    Value = "osh-match",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        var (md5Items, md5Count) = await repository.FindAsync(
            new SceneFilter
            {
                FingerprintCriterion = new FingerprintCriterion
                {
                    Type = "md5",
                    Value = "md5-match",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        var (phashItems, phashCount) = await repository.FindAsync(
            new SceneFilter
            {
                FingerprintCriterion = new FingerprintCriterion
                {
                    Type = "phash",
                    Value = "phash-match",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 50 });

        Assert.Equal(1, oshashCount);
        Assert.Equal(["matching-fingerprint-types"], oshashItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(1, md5Count);
        Assert.Equal(["matching-fingerprint-types"], md5Items.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(1, phashCount);
        Assert.Equal(["matching-fingerprint-types"], phashItems.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task DuplicatedPhashCriterion_True_FindsScenesSharingAPhashAcrossScenes()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("duplicate-a", fingerprints: [new FileFingerprint { Type = "phash", Value = "same-phash" }]),
            CreateSceneWithFile("duplicate-b", fingerprints: [new FileFingerprint { Type = "phash", Value = "same-phash" }]),
            CreateSceneWithFile("unique", fingerprints: [new FileFingerprint { Type = "phash", Value = "unique-phash" }]));
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);
        var filter = new SceneFilter
        {
            DuplicatedPhashCriterion = new BoolCriterion { Value = true },
        };

        var (items, totalCount) = await repository.FindAsync(filter, new FindFilter { Page = 1, PerPage = 50, Sort = "title" });

        Assert.Equal(2, totalCount);
        Assert.Equal(["duplicate-a", "duplicate-b"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task ScenesController_Find_BindsSeedFromQuery()
    {
        var repository = new CapturingSceneRepository();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        await using var context = CreateContext();
        var controller = new ScenesController(repository, context, null!, null!, null!, memoryCache, null!, null!, null!, new NoOpUserEngagementService(), new CustomFieldService(context));

        await controller.Find(q: null, page: 1, perPage: 25, sort: "random", direction: "desc", seed: 12345, ct: default);

        Assert.Equal(12345, repository.LastFindFilter?.Seed);
        Assert.Equal("random", repository.LastFindFilter?.Sort);
        Assert.Equal(Cove.Core.Enums.SortDirection.Desc, repository.LastFindFilter?.Direction);
    }

    [Fact]
    public async Task ScenesController_FindWithCompilations_ReturnsSceneRangeGroupsAsPagedRows()
    {
        await using var context = CreateContext();
        var scene = CreateSceneWithFile("scene row");
        scene.CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        scene.UpdatedAt = scene.CreatedAt;
        context.Scenes.Add(scene);
        await context.SaveChangesAsync();

        context.Groups.AddRange(
            new Group
            {
                Name = "compilation row",
                CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ShowInSceneLists = true,
                GroupItems = [new GroupItem { Kind = GroupItemKind.SceneRange, SceneId = scene.Id, HostId = scene.Id, StartSec = 10, EndSec = 20 }],
            },
            new Group
            {
                Name = "ordinary scene group",
                CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ShowInSceneLists = true,
                GroupItems = [new GroupItem { Kind = GroupItemKind.Scene, SceneId = scene.Id, HostId = scene.Id }],
            },
            new Group
            {
                Name = "hidden compilation",
                CreatedAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                ShowInSceneLists = false,
                GroupItems = [new GroupItem { Kind = GroupItemKind.SceneRange, SceneId = scene.Id, HostId = scene.Id, StartSec = 20, EndSec = 30 }],
            });
        await context.SaveChangesAsync();

        var controller = CreateScenesController(context);

        var response = await controller.FindWithCompilations(
            q: null, page: 1, perPage: 10, sort: "created_at", direction: "desc", seed: null,
            title: null, rating: null, organized: null, studioId: null, groupId: null, galleryId: null,
            tagIds: null, performerIds: null, ct: default);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PaginatedResponse<SceneListEntryDto>>(ok.Value);

        Assert.Equal(2, payload.TotalCount);
        Assert.Equal(["compilation", "scene"], payload.Items.Select(item => item.Kind).ToArray());
        Assert.Equal("compilation row", payload.Items[0].Group?.Name);
        Assert.True(payload.Items[0].Group?.IsCompilation);
        Assert.Equal("scene row", payload.Items[1].Scene?.Title);
    }

    [Fact]
    public async Task ScenesController_FindDuplicates_ExactFingerprint_UsesMd5AndOshash()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("md5 duplicate a", basename: "a.mp4", fingerprints: [new FileFingerprint { Type = "md5", Value = "same-md5" }]),
            CreateSceneWithFile("md5 duplicate b", basename: "b.mp4", fingerprints: [new FileFingerprint { Type = "md5", Value = "same-md5" }]),
            CreateSceneWithFile("oshash duplicate a", basename: "c.mp4", fingerprints: [new FileFingerprint { Type = "oshash", Value = "same-oshash" }]),
            CreateSceneWithFile("oshash duplicate b", basename: "d.mp4", fingerprints: [new FileFingerprint { Type = "oshash", Value = "same-oshash" }]),
            CreateSceneWithFile("unique", basename: "e.mp4", fingerprints: [new FileFingerprint { Type = "md5", Value = "unique-md5" }]));
        await context.SaveChangesAsync();

        var controller = CreateScenesController(context);

        var response = await controller.FindDuplicates(matchType: "fingerprint", ct: default);

        var groups = GetDuplicateGroups(response);
        Assert.Contains(groups, group => group.Select(scene => scene.Title ?? "").OrderBy(title => title).SequenceEqual(["md5 duplicate a", "md5 duplicate b"]));
        Assert.Contains(groups, group => group.Select(scene => scene.Title ?? "").OrderBy(title => title).SequenceEqual(["oshash duplicate a", "oshash duplicate b"]));
        Assert.DoesNotContain(groups.SelectMany(group => group), scene => scene.Title == "unique");
    }

    [Fact]
    public async Task ScenesController_FindDuplicates_Phash_UsesDistanceAndDurationTolerance()
    {
        await using var context = CreateContext();
        context.Scenes.AddRange(
            CreateSceneWithFile("visual duplicate a", basename: "a.mp4", fingerprints: [new FileFingerprint { Type = "phash", Value = "0000000000000000" }]),
            CreateSceneWithFile("visual duplicate b", basename: "b.mp4", fingerprints: [new FileFingerprint { Type = "phash", Value = "0000000000000001" }]),
            CreateSceneWithFile("different visual", basename: "c.mp4", fingerprints: [new FileFingerprint { Type = "phash", Value = "ffffffffffffffff" }]));
        await context.SaveChangesAsync();

        var controller = CreateScenesController(context);

        var response = await controller.FindDuplicates(matchType: "phash", distance: 1, durationDiff: 0, ct: default);

        var groups = GetDuplicateGroups(response);
        var group = Assert.Single(groups);
        Assert.Equal(["visual duplicate a", "visual duplicate b"], group.Select(scene => scene.Title ?? "").OrderBy(title => title).ToArray());
    }

    [Fact]
    public async Task LastPlayedAtSort_Descending_PutsPlayedScenesBeforeUnplayedScenes()
    {
        await using var context = CreateContext();
        var neverPlayed = new Scene { Title = "never-played" };
        var olderPlay = new Scene { Title = "older-play" };
        var recentPlay = new Scene { Title = "recent-play" };
        context.Scenes.AddRange(neverPlayed, olderPlay, recentPlay);
        await context.SaveChangesAsync();

        context.UserEntityAffinities.AddRange(
            new UserEntityAffinity { UserId = 1, HostType = AffinityHostType.Scene, HostId = olderPlay.Id, LastConsumedAt = new DateTime(2024, 1, 10, 8, 0, 0, DateTimeKind.Utc) },
            new UserEntityAffinity { UserId = 1, HostType = AffinityHostType.Scene, HostId = recentPlay.Id, LastConsumedAt = new DateTime(2024, 1, 12, 8, 0, 0, DateTimeKind.Utc) });
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);

        var (items, totalCount) = await repository.FindAsync(
            filter: null,
            new FindFilter
            {
                Page = 1,
                PerPage = 50,
                Sort = "last_played_at",
                Direction = Cove.Core.Enums.SortDirection.Desc,
            });

        Assert.Equal(3, totalCount);
        Assert.Equal(["recent-play", "older-play", "never-played"], items.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    private static Scene CreateSceneWithFile(
        string title,
        string? director = null,
        DateOnly? sceneDate = null,
        string folderPath = @"C:\library",
        string basename = "clip.mp4",
        string audioCodec = "AAC",
        string videoCodec = "H264",
        long bitRate = 1_000_000,
        Performer? performer = null,
        IEnumerable<FileFingerprint>? fingerprints = null)
    {
        var scene = new Scene
        {
            Title = title,
            Director = director,
            Date = sceneDate ?? new DateOnly(2024, 1, 1),
        };

        var file = new VideoFile
        {
            Basename = basename,
            ParentFolder = new Folder { Path = folderPath, ModTime = DateTime.UtcNow },
            AudioCodec = audioCodec,
            VideoCodec = videoCodec,
            BitRate = bitRate,
            FrameRate = 30,
            Duration = 120,
            Width = 1920,
            Height = 1080,
            Format = "mp4",
            Size = 1024,
            ModTime = DateTime.UtcNow,
        };

        if (fingerprints != null)
        {
            foreach (var fingerprint in fingerprints)
            {
                file.Fingerprints.Add(fingerprint);
            }
        }

        scene.Files.Add(file);

        if (performer != null)
        {
            scene.ScenePerformers.Add(new ScenePerformer { Performer = performer });
        }

        return scene;
    }

    private static Performer CreatePerformer(string name, DateOnly birthdate, params Tag[] tags)
    {
        var performer = new Performer
        {
            Name = name,
            Birthdate = birthdate,
        };

        foreach (var tag in tags)
        {
            performer.PerformerTags.Add(new PerformerTag { Performer = performer, Tag = tag });
        }

        return performer;
    }

    private static TagApplication CreateDurationApplication(int sceneId, int tagId, double totalDurationSec, double hostDurationSec)
        => new()
        {
            HostType = AffinityHostType.Scene,
            HostId = sceneId,
            TagId = tagId,
            TotalDurationSec = totalDurationSec,
            HostDurationSec = hostDurationSec,
            SourceKey = "test",
        };

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"scene-filter-behavior-{Guid.NewGuid():N}")
            .Options;

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(new CovePrincipal
        {
            UserId = 1,
            Username = "test-user",
            Kind = PrincipalKind.User,
            Permissions = new HashSet<string> { "*" },
            Roles = new HashSet<string>(),
        });

        return new TestCoveContext(options, principalAccessor);
    }

    private static ScenesController CreateScenesController(CoveContext context)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new ScenesController(new CapturingSceneRepository(), context, null!, null!, null!, memoryCache, null!, null!, null!, new NoOpUserEngagementService(), new CustomFieldService(context));
    }

    private static List<List<SceneDto>> GetDuplicateGroups(ActionResult<List<List<SceneDto>>> response)
    {
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        return Assert.IsType<List<List<SceneDto>>>(ok.Value);
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor) : CoveContext(options, principalAccessor)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }

    private sealed class CapturingSceneRepository : ISceneRepository
    {
        public FindFilter? LastFindFilter { get; private set; }

        public Task<(IReadOnlyList<Scene> Items, int TotalCount)> FindAsync(SceneFilter? filter, FindFilter? findFilter, CancellationToken ct = default)
        {
            LastFindFilter = findFilter;
            return Task.FromResult<(IReadOnlyList<Scene>, int)>((Array.Empty<Scene>(), 0));
        }

        public Task<Scene?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Scene>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Scene> AddAsync(Scene entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Scene entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Scene?> GetByIdWithRelationsAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
