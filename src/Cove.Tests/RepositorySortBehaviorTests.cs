using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cove.Tests;

public class RepositorySortBehaviorTests
{
    [Fact]
    public async Task TagRepository_SceneCountSort_UsesSceneAssociations()
    {
        await using var context = CreateContext();
        var busy = new Tag { Name = "Busy", CreatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc) };
        var quiet = new Tag { Name = "Quiet", CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        context.Scenes.AddRange(
            CreateSceneWithTag("first", busy),
            CreateSceneWithTag("second", busy),
            CreateSceneWithTag("third", quiet));
        await context.SaveChangesAsync();

        var repository = new TagRepository(context);
        var (items, totalCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "scene_count", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(2, totalCount);
        Assert.Equal(["Busy", "Quiet"], items.Select(tag => tag.Name).ToArray());
    }

    [Fact]
    public async Task TagRepository_SupportsRequestedCountAndUpdatedAtSorts()
    {
        await using var context = CreateContext();

        var countsLeader = new Tag
        {
            Name = "Counts Leader",
            UpdatedAt = new DateTime(2024, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        };

        var lighter = new Tag
        {
            Name = "Lighter",
            UpdatedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        };

        var quiet = new Tag
        {
            Name = "Quiet",
            UpdatedAt = new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc),
        };

        countsLeader.ImageTags.Add(new ImageTag { Tag = countsLeader, Image = new Image { Title = "image-1" } });
        countsLeader.ImageTags.Add(new ImageTag { Tag = countsLeader, Image = new Image { Title = "image-2" } });
        countsLeader.GalleryTags.Add(new GalleryTag { Tag = countsLeader, Gallery = new Gallery { Title = "gallery-1" } });
        countsLeader.GalleryTags.Add(new GalleryTag { Tag = countsLeader, Gallery = new Gallery { Title = "gallery-2" } });
        countsLeader.GroupTags.Add(new GroupTag { Tag = countsLeader, Group = new Group { Name = "group-1" } });
        countsLeader.GroupTags.Add(new GroupTag { Tag = countsLeader, Group = new Group { Name = "group-2" } });
        countsLeader.PerformerTags.Add(new PerformerTag { Tag = countsLeader, Performer = new Performer { Name = "performer-1" } });
        countsLeader.PerformerTags.Add(new PerformerTag { Tag = countsLeader, Performer = new Performer { Name = "performer-2" } });
        countsLeader.StudioTags.Add(new StudioTag { Tag = countsLeader, Studio = new Studio { Name = "studio-1" } });
        countsLeader.StudioTags.Add(new StudioTag { Tag = countsLeader, Studio = new Studio { Name = "studio-2" } });

        lighter.ImageTags.Add(new ImageTag { Tag = lighter, Image = new Image { Title = "image-3" } });
        lighter.GalleryTags.Add(new GalleryTag { Tag = lighter, Gallery = new Gallery { Title = "gallery-3" } });
        lighter.GroupTags.Add(new GroupTag { Tag = lighter, Group = new Group { Name = "group-3" } });
        lighter.PerformerTags.Add(new PerformerTag { Tag = lighter, Performer = new Performer { Name = "performer-3" } });
        lighter.StudioTags.Add(new StudioTag { Tag = lighter, Studio = new Studio { Name = "studio-3" } });

        context.Tags.AddRange(countsLeader, lighter, quiet);
        await context.SaveChangesAsync();

        var repository = new TagRepository(context);

        var (galleryItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "gallery_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (groupItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "group_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (imageItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "image_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (performerItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "performer_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (studioItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "studio_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (updatedItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "updated_at", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], galleryItems.Select(tag => tag.Name).ToArray());
        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], groupItems.Select(tag => tag.Name).ToArray());
        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], imageItems.Select(tag => tag.Name).ToArray());
        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], performerItems.Select(tag => tag.Name).ToArray());
        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], studioItems.Select(tag => tag.Name).ToArray());
        Assert.Equal(["Counts Leader", "Lighter", "Quiet"], updatedItems.Select(tag => tag.Name).ToArray());
    }

    [Fact]
    public async Task StudioRepository_SceneCountSort_UsesSceneAssociations()
    {
        await using var context = CreateContext();
        var busiest = new Studio { Name = "Busiest", CreatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc) };
        var quieter = new Studio { Name = "Quieter", CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        busiest.Scenes.Add(new Scene { Title = "one" });
        busiest.Scenes.Add(new Scene { Title = "two" });
        quieter.Scenes.Add(new Scene { Title = "three" });

        context.Studios.AddRange(busiest, quieter);
        await context.SaveChangesAsync();

        var repository = new StudioRepository(context);
        var (items, totalCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "scene_count", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(2, totalCount);
        Assert.Equal(["Busiest", "Quieter"], items.Select(studio => studio.Name).ToArray());
    }

    [Fact]
    public async Task StudioRepository_SupportsRequestedCountRatingAndUpdatedAtSorts()
    {
        await using var context = CreateContext();

        var alphaTag = new Tag { Name = "Alpha Tag" };
        var betaTag = new Tag { Name = "Beta Tag" };

        var highestRated = new Studio
        {
            Name = "Highest Rated",
            UpdatedAt = new DateTime(2024, 1, 12, 0, 0, 0, DateTimeKind.Utc),
        };

        var countsLeader = new Studio
        {
            Name = "Counts Leader",
            UpdatedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        };

        var unrated = new Studio
        {
            Name = "Unrated",
            UpdatedAt = new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc),
        };

        countsLeader.Galleries.Add(new Gallery { Title = "g1" });
        countsLeader.Galleries.Add(new Gallery { Title = "g2" });
        countsLeader.Images.Add(new Image { Title = "i1" });
        countsLeader.Images.Add(new Image { Title = "i2" });
        countsLeader.Children.Add(new Studio { Name = "Child Studio" });
        countsLeader.StudioTags.Add(new StudioTag { Studio = countsLeader, Tag = alphaTag });
        countsLeader.StudioTags.Add(new StudioTag { Studio = countsLeader, Tag = betaTag });

        highestRated.Galleries.Add(new Gallery { Title = "g3" });
        highestRated.Images.Add(new Image { Title = "i3" });

        unrated.Images.Add(new Image { Title = "i4" });

        context.Studios.AddRange(highestRated, countsLeader, unrated);
        await context.SaveChangesAsync();

        AddRating(context, RatingHostType.Studio, highestRated.Id, 95);
        AddRating(context, RatingHostType.Studio, countsLeader.Id, 40);
        await context.SaveChangesAsync();

        var repository = new StudioRepository(context);

        var (galleryItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "gallery_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (imageItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "image_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (ratingItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (childItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "child_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (tagItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "tag_count", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (updatedItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "updated_at", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(["Counts Leader", "Highest Rated", "Unrated"], galleryItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Counts Leader", "Highest Rated", "Unrated"], imageItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Highest Rated", "Counts Leader", "Unrated"], ratingItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Counts Leader", "Highest Rated", "Unrated"], childItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Counts Leader", "Highest Rated", "Unrated"], tagItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Highest Rated", "Counts Leader", "Unrated"], updatedItems.Select(studio => studio.Name).ToArray());
    }

    [Fact]
    public async Task GalleryRepository_KeepsUnratedItemsLastAndMatchesFolderBackedPaths()
    {
        await using var context = CreateContext();

        var ratedFolderGallery = new Gallery
        {
            Title = "folder-gallery",
            Folder = new Folder { Path = @"C:\library\matched-folder", ModTime = new DateTime(2024, 1, 12, 0, 0, 0, DateTimeKind.Utc) },
        };

        var unratedFileGallery = new Gallery
        {
            Title = "file-gallery",
        };
        unratedFileGallery.Files.Add(new GalleryFile
        {
            Basename = "set.zip",
            ParentFolder = new Folder { Path = @"C:\library\other-folder", ModTime = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc) },
            ModTime = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Size = 1024,
        });

        context.Galleries.AddRange(ratedFolderGallery, unratedFileGallery);
        await context.SaveChangesAsync();

        AddRating(context, RatingHostType.Gallery, ratedFolderGallery.Id, 80);
        await context.SaveChangesAsync();

        var repository = new GalleryRepository(context);

        var (ratingItems, ratingCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Desc });

        var (pathItems, pathCount) = await repository.FindAsync(
            new GalleryFilter
            {
                PathCriterion = new StringCriterion
                {
                    Value = @"C:\library\matched-folder",
                    Modifier = CriterionModifier.Equals,
                },
            },
            new FindFilter { Page = 1, PerPage = 20, Sort = "title", Direction = Cove.Core.Enums.SortDirection.Asc });

        Assert.Equal(2, ratingCount);
        Assert.Equal(["folder-gallery", "file-gallery"], ratingItems.Select(gallery => gallery.Title ?? string.Empty).ToArray());
        Assert.Equal(1, pathCount);
        Assert.Equal(["folder-gallery"], pathItems.Select(gallery => gallery.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task GroupRepository_SupportsDateRatingAndCreatedAtSorts()
    {
        await using var context = CreateContext();
        var newest = new Group
        {
            Name = "Newest",
            Date = new DateOnly(2024, 1, 12),
        };
        var older = new Group
        {
            Name = "Older",
            Date = new DateOnly(2024, 1, 10),
        };
        var undated = new Group
        {
            Name = "Undated",
        };

        context.Groups.AddRange(newest, older, undated);
        await context.SaveChangesAsync();

        AddRating(context, RatingHostType.Group, newest.Id, 90);
        AddRating(context, RatingHostType.Group, older.Id, 50);
        await context.SaveChangesAsync();

        newest.CreatedAt = new DateTime(2024, 1, 12, 0, 0, 0, DateTimeKind.Utc);
        older.CreatedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        undated.CreatedAt = new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        var repository = new GroupRepository(context);

        var (dateItems, dateCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "date", Direction = Cove.Core.Enums.SortDirection.Desc });

        var (ratingItems, ratingCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Desc });

        var (createdItems, createdCount) = await repository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "created_at", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(3, dateCount);
        Assert.Equal(["Newest", "Older", "Undated"], dateItems.Select(group => group.Name).ToArray());
        Assert.Equal(3, ratingCount);
        Assert.Equal(["Newest", "Older", "Undated"], ratingItems.Select(group => group.Name).ToArray());
        Assert.Equal(3, createdCount);
        Assert.Equal(["Newest", "Older", "Undated"], createdItems.Select(group => group.Name).ToArray());
    }

    [Fact]
    public async Task PerformerRepository_SupportsComputedCareerFavoritePlaybackMeasurementHeightAndPlayCountSorts()
    {
        await using var context = CreateContext();

        var leader = new Performer
        {
            Name = "Leader",
            HeightCm = 190,
            Measurements = "100A-24-36",
            CareerStart = new DateOnly(2010, 1, 1),
            CareerEnd = new DateOnly(2024, 1, 1),
        };

        var middle = new Performer
        {
            Name = "Middle",
            HeightCm = 165,
            Measurements = "32B-24-32",
            CareerStart = new DateOnly(2018, 1, 1),
            CareerEnd = new DateOnly(2024, 1, 1),
        };

        var compact = new Performer
        {
            Name = "Compact",
            HeightCm = 150,
            Measurements = "9A-23-30",
            CareerStart = new DateOnly(2020, 1, 1),
            CareerEnd = new DateOnly(2024, 1, 1),
        };

        var quiet = new Performer
        {
            Name = "Quiet",
            HeightCm = 0,
        };

        var leaderScene = new Scene
        {
            Title = "leader-scene",
        };
        leaderScene.ScenePerformers.Add(new ScenePerformer { Scene = leaderScene, Performer = leader });
        leaderScene.LikeHistory.Add(new SceneLikeHistory { OccurredAt = new DateTime(2024, 1, 16, 12, 0, 0, DateTimeKind.Utc) });

        var middleScene = new Scene
        {
            Title = "middle-scene",
        };
        middleScene.ScenePerformers.Add(new ScenePerformer { Scene = middleScene, Performer = middle });
        middleScene.LikeHistory.Add(new SceneLikeHistory { OccurredAt = new DateTime(2024, 1, 11, 12, 0, 0, DateTimeKind.Utc) });

        var compactScene = new Scene
        {
            Title = "compact-scene",
        };
        compactScene.ScenePerformers.Add(new ScenePerformer { Scene = compactScene, Performer = compact });
        compactScene.LikeHistory.Add(new SceneLikeHistory { OccurredAt = new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc) });

        context.Performers.AddRange(leader, middle, compact, quiet);
        context.Scenes.AddRange(leaderScene, middleScene, compactScene);
        await context.SaveChangesAsync();

        AddSceneAffinity(context, leaderScene.Id, viewCount: 12, likeCount: 8, lastConsumedAt: new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        AddSceneAffinity(context, middleScene.Id, viewCount: 4, likeCount: 2, lastConsumedAt: new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        AddSceneAffinity(context, compactScene.Id, viewCount: 1, likeCount: 1, lastConsumedAt: new DateTime(2024, 1, 5, 12, 0, 0, DateTimeKind.Utc));
        AddLikeInteraction(context, leaderScene.Id, new DateTime(2024, 1, 16, 12, 0, 0, DateTimeKind.Utc));
        AddLikeInteraction(context, middleScene.Id, new DateTime(2024, 1, 11, 12, 0, 0, DateTimeKind.Utc));
        AddLikeInteraction(context, compactScene.Id, new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc));
        await context.SaveChangesAsync();

        var repository = new PerformerRepository(context);

        var (careerItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "career_length", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (favoriteItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "last_like_at", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (playedItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "last_played_at", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (heightDescItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "height", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (heightAscItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "height", Direction = Cove.Core.Enums.SortDirection.Asc });
        var (measurementItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "measurements", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (favoritesItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "like_counter", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (playCountItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "play_count", Direction = Cove.Core.Enums.SortDirection.Desc });

        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], careerItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], favoriteItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], playedItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], heightDescItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Compact", "Middle", "Leader", "Quiet"], heightAscItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], measurementItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], favoritesItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Leader", "Middle", "Compact", "Quiet"], playCountItems.Select(performer => performer.Name).ToArray());
    }

    [Fact]
    public async Task RatingSorts_PlaceUnratedAndZeroRatedItemsFirstWhenSortingAscending()
    {
        await using var context = CreateContext();

        context.Performers.AddRange(
            new Performer { Name = "Performer Low" },
            new Performer { Name = "Performer High" },
            new Performer { Name = "Performer Zero" },
            new Performer { Name = "Performer Unrated" });

        context.Images.AddRange(
            new Image { Title = "Image Low" },
            new Image { Title = "Image High" },
            new Image { Title = "Image Zero" },
            new Image { Title = "Image Unrated" });

        context.Groups.AddRange(
            new Group { Name = "Group Low" },
            new Group { Name = "Group High" },
            new Group { Name = "Group Zero" },
            new Group { Name = "Group Unrated" });

        context.Studios.AddRange(
            new Studio { Name = "Studio Low" },
            new Studio { Name = "Studio High" },
            new Studio { Name = "Studio Zero" },
            new Studio { Name = "Studio Unrated" });

        context.Galleries.AddRange(
            new Gallery { Title = "Gallery Low" },
            new Gallery { Title = "Gallery High" },
            new Gallery { Title = "Gallery Zero" },
            new Gallery { Title = "Gallery Unrated" });

        context.Scenes.AddRange(
            new Scene { Title = "Scene Low" },
            new Scene { Title = "Scene High" },
            new Scene { Title = "Scene Zero" },
            new Scene { Title = "Scene Unrated" });

        await context.SaveChangesAsync();

        AddRating(context, RatingHostType.Performer, context.Performers.Single(entity => entity.Name == "Performer Low").Id, 20);
        AddRating(context, RatingHostType.Performer, context.Performers.Single(entity => entity.Name == "Performer High").Id, 80);
        AddRating(context, RatingHostType.Performer, context.Performers.Single(entity => entity.Name == "Performer Zero").Id, 0);
        AddRating(context, RatingHostType.Image, context.Images.Single(entity => entity.Title == "Image Low").Id, 20);
        AddRating(context, RatingHostType.Image, context.Images.Single(entity => entity.Title == "Image High").Id, 80);
        AddRating(context, RatingHostType.Image, context.Images.Single(entity => entity.Title == "Image Zero").Id, 0);
        AddRating(context, RatingHostType.Group, context.Groups.Single(entity => entity.Name == "Group Low").Id, 20);
        AddRating(context, RatingHostType.Group, context.Groups.Single(entity => entity.Name == "Group High").Id, 80);
        AddRating(context, RatingHostType.Group, context.Groups.Single(entity => entity.Name == "Group Zero").Id, 0);
        AddRating(context, RatingHostType.Studio, context.Studios.Single(entity => entity.Name == "Studio Low").Id, 20);
        AddRating(context, RatingHostType.Studio, context.Studios.Single(entity => entity.Name == "Studio High").Id, 80);
        AddRating(context, RatingHostType.Studio, context.Studios.Single(entity => entity.Name == "Studio Zero").Id, 0);
        AddRating(context, RatingHostType.Gallery, context.Galleries.Single(entity => entity.Title == "Gallery Low").Id, 20);
        AddRating(context, RatingHostType.Gallery, context.Galleries.Single(entity => entity.Title == "Gallery High").Id, 80);
        AddRating(context, RatingHostType.Gallery, context.Galleries.Single(entity => entity.Title == "Gallery Zero").Id, 0);
        AddRating(context, RatingHostType.Scene, context.Scenes.Single(entity => entity.Title == "Scene Low").Id, 20);
        AddRating(context, RatingHostType.Scene, context.Scenes.Single(entity => entity.Title == "Scene High").Id, 80);
        AddRating(context, RatingHostType.Scene, context.Scenes.Single(entity => entity.Title == "Scene Zero").Id, 0);
        await context.SaveChangesAsync();

        var performerRepository = new PerformerRepository(context);
        var imageRepository = new ImageRepository(context);
        var groupRepository = new GroupRepository(context);
        var studioRepository = new StudioRepository(context);
        var galleryRepository = new GalleryRepository(context);
        var sceneRepository = new SceneRepository(context);

        var (performerItems, _) = await performerRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        var (imageItems, _) = await imageRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        var (groupItems, _) = await groupRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        var (studioItems, _) = await studioRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        var (galleryItems, _) = await galleryRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        var (sceneItems, _) = await sceneRepository.FindAsync(
            filter: null,
            new FindFilter { Page = 1, PerPage = 20, Sort = "rating", Direction = Cove.Core.Enums.SortDirection.Asc });

        Assert.Equal(["Performer Unrated", "Performer Zero", "Performer Low", "Performer High"], performerItems.Select(performer => performer.Name).ToArray());
        Assert.Equal(["Image Unrated", "Image Zero", "Image Low", "Image High"], imageItems.Select(image => image.Title ?? string.Empty).ToArray());
        Assert.Equal(["Group Unrated", "Group Zero", "Group Low", "Group High"], groupItems.Select(group => group.Name).ToArray());
        Assert.Equal(["Studio Unrated", "Studio Zero", "Studio Low", "Studio High"], studioItems.Select(studio => studio.Name).ToArray());
        Assert.Equal(["Gallery Unrated", "Gallery Zero", "Gallery Low", "Gallery High"], galleryItems.Select(gallery => gallery.Title ?? string.Empty).ToArray());
        Assert.Equal(["Scene Unrated", "Scene Zero", "Scene Low", "Scene High"], sceneItems.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task SceneRepository_SupportsParitySceneSorts()
    {
        await using var context = CreateContext();

        var alphaStudio = new Studio { Name = "Alpha Studio" };
        var betaStudio = new Studio { Name = "Beta Studio" };

        var youngerPerformer = new Performer { Name = "Younger", Birthdate = new DateOnly(2004, 1, 1) };
        var olderPerformer = new Performer { Name = "Older", Birthdate = new DateOnly(1984, 1, 1) };

        var alphaScene = CreateSceneWithFile(
            "alpha-scene",
            folderPath: @"C:\library\a",
            basename: "a.mp4",
            fileModTime: new DateTime(2024, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            code: "A-002",
            studio: alphaStudio,
            performer: youngerPerformer,
            fingerprints: [new FileFingerprint { Type = "phash", Value = "00aa" }]);
        alphaScene.Date = new DateOnly(2024, 1, 20);
        alphaScene.LikeHistory.Add(new SceneLikeHistory { OccurredAt = new DateTime(2024, 1, 5, 12, 0, 0, DateTimeKind.Utc) });

        var betaScene = CreateSceneWithFile(
            "beta-scene",
            folderPath: @"C:\library\z",
            basename: "z.mp4",
            fileModTime: new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            code: "B-001",
            studio: betaStudio,
            performer: olderPerformer,
            fingerprints: [new FileFingerprint { Type = "phash", Value = "00ff" }]);
        betaScene.Date = new DateOnly(2024, 1, 20);
        betaScene.LikeHistory.Add(new SceneLikeHistory { OccurredAt = new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc) });

        context.Scenes.AddRange(alphaScene, betaScene);
        await context.SaveChangesAsync();

        var repository = new SceneRepository(context);

        var (fileModItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "file_mod_time", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (favoriteItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "last_like_at", Direction = Cove.Core.Enums.SortDirection.Desc });
        var (pathItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "path", Direction = Cove.Core.Enums.SortDirection.Asc });
        var (phashItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "phash", Direction = Cove.Core.Enums.SortDirection.Asc });
        var (ageItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "performer_age", Direction = Cove.Core.Enums.SortDirection.Asc });
        var (studioItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "studio", Direction = Cove.Core.Enums.SortDirection.Asc });
        var (codeItems, _) = await repository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "code", Direction = Cove.Core.Enums.SortDirection.Asc });

        Assert.Equal(["beta-scene", "alpha-scene"], fileModItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["beta-scene", "alpha-scene"], favoriteItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["alpha-scene", "beta-scene"], pathItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["alpha-scene", "beta-scene"], phashItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["alpha-scene", "beta-scene"], ageItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["alpha-scene", "beta-scene"], studioItems.Select(scene => scene.Title ?? string.Empty).ToArray());
        Assert.Equal(["alpha-scene", "beta-scene"], codeItems.Select(scene => scene.Title ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task RandomSort_RespectsAscendingAndDescendingAcrossRepositories()
    {
        await using var context = CreateContext();

        context.Performers.AddRange(
            new Performer { Name = "Performer One" },
            new Performer { Name = "Performer Two" },
            new Performer { Name = "Performer Three" },
            new Performer { Name = "Performer Four" },
            new Performer { Name = "Performer Five" },
            new Performer { Name = "Performer Six" });
        context.Tags.AddRange(
            new Tag { Name = "Tag One" },
            new Tag { Name = "Tag Two" },
            new Tag { Name = "Tag Three" },
            new Tag { Name = "Tag Four" },
            new Tag { Name = "Tag Five" },
            new Tag { Name = "Tag Six" });
        context.Studios.AddRange(
            new Studio { Name = "Studio One" },
            new Studio { Name = "Studio Two" },
            new Studio { Name = "Studio Three" },
            new Studio { Name = "Studio Four" },
            new Studio { Name = "Studio Five" },
            new Studio { Name = "Studio Six" });
        context.Galleries.AddRange(
            new Gallery { Title = "Gallery One" },
            new Gallery { Title = "Gallery Two" },
            new Gallery { Title = "Gallery Three" },
            new Gallery { Title = "Gallery Four" },
            new Gallery { Title = "Gallery Five" },
            new Gallery { Title = "Gallery Six" });
        context.Images.AddRange(
            new Image { Title = "Image One" },
            new Image { Title = "Image Two" },
            new Image { Title = "Image Three" },
            new Image { Title = "Image Four" },
            new Image { Title = "Image Five" },
            new Image { Title = "Image Six" });
        context.Groups.AddRange(
            new Group { Name = "Group One" },
            new Group { Name = "Group Two" },
            new Group { Name = "Group Three" },
            new Group { Name = "Group Four" },
            new Group { Name = "Group Five" },
            new Group { Name = "Group Six" });
        context.Scenes.AddRange(
            new Scene { Title = "Scene One" },
            new Scene { Title = "Scene Two" },
            new Scene { Title = "Scene Three" },
            new Scene { Title = "Scene Four" },
            new Scene { Title = "Scene Five" },
            new Scene { Title = "Scene Six" });
        await context.SaveChangesAsync();

        const int seed = 17;
        var performerRepository = new PerformerRepository(context);
        var tagRepository = new TagRepository(context);
        var studioRepository = new StudioRepository(context);
        var galleryRepository = new GalleryRepository(context);
        var imageRepository = new ImageRepository(context);
        var groupRepository = new GroupRepository(context);
        var sceneRepository = new SceneRepository(context);

        var (performerAsc, _) = await performerRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (performerDesc, _) = await performerRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (tagAsc, _) = await tagRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (tagDesc, _) = await tagRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (studioAsc, _) = await studioRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (studioDesc, _) = await studioRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (galleryAsc, _) = await galleryRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (galleryDesc, _) = await galleryRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (imageAsc, _) = await imageRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (imageDesc, _) = await imageRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (groupAsc, _) = await groupRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (groupDesc, _) = await groupRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });
        var (sceneAsc, _) = await sceneRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Asc, Seed = seed });
        var (sceneDesc, _) = await sceneRepository.FindAsync(null, new FindFilter { Page = 1, PerPage = 20, Sort = "random", Direction = Cove.Core.Enums.SortDirection.Desc, Seed = seed });

        Assert.NotEqual(["Performer One", "Performer Two", "Performer Three", "Performer Four", "Performer Five", "Performer Six"], performerAsc.Select(item => item.Name).ToArray());
        Assert.NotEqual(["Tag One", "Tag Two", "Tag Three", "Tag Four", "Tag Five", "Tag Six"], tagAsc.Select(item => item.Name).ToArray());
        Assert.NotEqual(["Studio One", "Studio Two", "Studio Three", "Studio Four", "Studio Five", "Studio Six"], studioAsc.Select(item => item.Name).ToArray());
        Assert.NotEqual(["Gallery One", "Gallery Two", "Gallery Three", "Gallery Four", "Gallery Five", "Gallery Six"], galleryAsc.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.NotEqual(["Image One", "Image Two", "Image Three", "Image Four", "Image Five", "Image Six"], imageAsc.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.NotEqual(["Group One", "Group Two", "Group Three", "Group Four", "Group Five", "Group Six"], groupAsc.Select(item => item.Name).ToArray());
        Assert.NotEqual(["Scene One", "Scene Two", "Scene Three", "Scene Four", "Scene Five", "Scene Six"], sceneAsc.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.Equal(performerAsc.Select(item => item.Name).Reverse().ToArray(), performerDesc.Select(item => item.Name).ToArray());
        Assert.Equal(tagAsc.Select(item => item.Name).Reverse().ToArray(), tagDesc.Select(item => item.Name).ToArray());
        Assert.Equal(studioAsc.Select(item => item.Name).Reverse().ToArray(), studioDesc.Select(item => item.Name).ToArray());
        Assert.Equal(galleryAsc.Select(item => item.Title ?? string.Empty).Reverse().ToArray(), galleryDesc.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.Equal(imageAsc.Select(item => item.Title ?? string.Empty).Reverse().ToArray(), imageDesc.Select(item => item.Title ?? string.Empty).ToArray());
        Assert.Equal(groupAsc.Select(item => item.Name).Reverse().ToArray(), groupDesc.Select(item => item.Name).ToArray());
        Assert.Equal(sceneAsc.Select(item => item.Title ?? string.Empty).Reverse().ToArray(), sceneDesc.Select(item => item.Title ?? string.Empty).ToArray());
    }

    private static Scene CreateSceneWithTag(string title, Tag tag)
    {
        var scene = new Scene { Title = title };
        scene.SceneTags.Add(new SceneTag { Scene = scene, Tag = tag });
        return scene;
    }

    private static Scene CreateSceneWithFile(
        string title,
        string folderPath,
        string basename,
        DateTime fileModTime,
        string code,
        Studio studio,
        Performer performer,
        IEnumerable<FileFingerprint> fingerprints)
    {
        var scene = new Scene
        {
            Title = title,
            Code = code,
            Studio = studio,
        };

        scene.ScenePerformers.Add(new ScenePerformer { Scene = scene, Performer = performer });

        var file = new VideoFile
        {
            Scene = scene,
            Basename = basename,
            ParentFolder = new Folder { Path = folderPath, ModTime = fileModTime },
            Format = "mp4",
            Width = 1920,
            Height = 1080,
            Duration = 120,
            VideoCodec = "h264",
            AudioCodec = "aac",
            FrameRate = 30,
            BitRate = 1_000_000,
            Size = 1024,
            ModTime = fileModTime,
        };

        foreach (var fingerprint in fingerprints)
        {
            file.Fingerprints.Add(fingerprint);
        }

        scene.Files.Add(file);

        return scene;
    }

    private const int TestUserId = 1;

    private static void AddRating(CoveContext context, RatingHostType hostType, int hostId, int? value)
    {
        if (value.HasValue)
            context.Ratings.Add(new Rating { UserId = TestUserId, HostType = hostType, HostId = hostId, Value = value.Value });
    }

    private static void AddSceneAffinity(CoveContext context, int sceneId, int viewCount = 0, int likeCount = 0, DateTime? lastConsumedAt = null)
    {
        context.UserEntityAffinities.Add(new UserEntityAffinity { UserId = TestUserId, HostType = AffinityHostType.Scene, HostId = sceneId, ViewCount = viewCount, LikeCount = likeCount, LastConsumedAt = lastConsumedAt });
    }

    private static void AddLikeInteraction(CoveContext context, int sceneId, DateTime at)
    {
        context.Interactions.Add(new Interaction { UserId = TestUserId, HostType = InteractionHostType.Scene, HostId = sceneId, Kind = InteractionKind.LikeCount, At = at });
    }

    private static CoveContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"repository-sort-behavior-{Guid.NewGuid():N}")
            .Options;

        var principalAccessor = new CurrentPrincipalAccessor();
        principalAccessor.Set(new CovePrincipal
        {
            UserId = TestUserId,
            Username = "test-user",
            Kind = PrincipalKind.User,
            Permissions = new HashSet<string> { "*" },
            Roles = new HashSet<string>(),
        });

        return new TestCoveContext(options, principalAccessor);
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor) : CoveContext(options, principalAccessor)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }
}
