using Cove.Core.Auth;
using Cove.Core.Entities;
using Microsoft.EntityFrameworkCore;
using PermissionKeys = Cove.Core.Auth.Permissions;

namespace Cove.Data;

public partial class CoveContext
{
    private readonly ICurrentPrincipalAccessor? _principalAccessor;

    private CovePrincipal? CurrentPrincipal => _principalAccessor?.Current;

    internal CovePrincipal? CurrentPrincipalForReadOptimization => CurrentPrincipal;

    private bool AuthorizationFiltersBypassed =>
        CurrentPrincipal is null ||
        CurrentPrincipal.Kind == PrincipalKind.System ||
        CurrentPrincipal.Has("*");

    internal bool AuthorizationBypassedForReadOptimization => AuthorizationFiltersBypassed;

    private string[] CurrentRoleNames => CurrentPrincipal?.Roles.ToArray() ?? [];

    private Guid? CurrentShareLinkId => CurrentPrincipal?.Kind == PrincipalKind.ShareLink ? CurrentPrincipal.TokenId : null;
    private int? CurrentUserId => CurrentPrincipal?.UserId;

    internal Guid? CurrentShareLinkIdForReadOptimization => CurrentShareLinkId;

    private bool CanReadScenes => CurrentPrincipal?.Has(PermissionKeys.ScenesRead) == true;
    private bool CanReadPerformers => CurrentPrincipal?.Has(PermissionKeys.PerformersRead) == true;
    private bool CanReadTags => CurrentPrincipal?.Has(PermissionKeys.TagsRead) == true;
    private bool CanReadStudios => CurrentPrincipal?.Has(PermissionKeys.StudiosRead) == true;
    private bool CanReadGalleries => CurrentPrincipal?.Has(PermissionKeys.GalleriesRead) == true;
    private bool CanReadImages => CurrentPrincipal?.Has(PermissionKeys.ImagesRead) == true;
    private bool CanReadGroups => CurrentPrincipal?.Has(PermissionKeys.GroupsRead) == true;
    private bool CanReadSegments => CurrentPrincipal?.Has(PermissionKeys.SegmentsRead) == true;
    private bool CanReadFaces => CurrentPrincipal?.Has(PermissionKeys.FacesRead) == true;
    private bool CanReadEmbeddings => CurrentPrincipal?.Has(PermissionKeys.EmbeddingsRead) == true;
    private bool CanReadAiRuns => CurrentPrincipal?.Has(PermissionKeys.AiRunsRead) == true;
    private bool CanReadScenesByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Scene) == true;
    private bool CanReadPerformersByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Performer) == true;
    private bool CanReadTagsByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Tag) == true;
    private bool CanReadStudiosByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Studio) == true;
    private bool CanReadGalleriesByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Gallery) == true;
    private bool CanReadImagesByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Image) == true;
    private bool CanReadGroupsByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Group) == true;
    private bool CanReadSegmentsByRule => CurrentPrincipal?.ReadGrantedEntityKinds.Contains(EntityKinds.Marker) == true;

    private bool RequiresSceneReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Scene) == true;
    private bool RequiresPerformerReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Performer) == true;
    private bool RequiresTagReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Tag) == true;
    private bool RequiresStudioReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Studio) == true;
    private bool RequiresGalleryReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Gallery) == true;
    private bool RequiresImageReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Image) == true;
    private bool RequiresGroupReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains(EntityKinds.Group) == true;
    private bool RequiresMarkerReadScopeEvaluation => CurrentShareLinkId != null || CurrentPrincipal?.ReadRestrictedEntityKinds.Contains("marker") == true;

    [DbFunction("cove_authz_can_read", "public")]
    public static bool CanReadEntitySql(
        bool bypassAuthorization,
        bool hasReadPermission,
        bool hasReadGrant,
        string[] roleNames,
        Guid? shareLinkId,
        string entityKind,
        int entityId)
        => throw new NotSupportedException();

    public IQueryable<TEntity> ReadSet<TEntity>() where TEntity : class
        => AuthorizationFiltersBypassed ? Set<TEntity>().IgnoreQueryFilters() : Set<TEntity>();

    private void ConfigureAuthorizationFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scene>().HasQueryFilter(scene =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, scene.Id));

        modelBuilder.Entity<Performer>().HasQueryFilter(performer =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, performer.Id));

        modelBuilder.Entity<Tag>().HasQueryFilter(tag =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, tag.Id));

        modelBuilder.Entity<Studio>().HasQueryFilter(studio =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresStudioReadScopeEvaluation
                    ? CanReadStudios
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadStudios, CanReadStudiosByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Studio, studio.Id));

        modelBuilder.Entity<Gallery>().HasQueryFilter(gallery =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, gallery.Id));

        modelBuilder.Entity<Image>().HasQueryFilter(image =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresImageReadScopeEvaluation
                    ? CanReadImages
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadImages, CanReadImagesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Image, image.Id));

        modelBuilder.Entity<Group>().HasQueryFilter(group =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, group.Id));

        modelBuilder.Entity<Face>().HasQueryFilter(face =>
            AuthorizationFiltersBypassed || CanReadFaces);

        modelBuilder.Entity<Embedding>().HasQueryFilter(embedding =>
            AuthorizationFiltersBypassed || CanReadEmbeddings);

        modelBuilder.Entity<AiRun>().HasQueryFilter(run =>
            AuthorizationFiltersBypassed || CanReadAiRuns);

        modelBuilder.Entity<UserEntityAffinity>().HasQueryFilter(affinity =>
            AuthorizationFiltersBypassed || (CurrentUserId != null && affinity.UserId == CurrentUserId));

        modelBuilder.Entity<Interaction>().HasQueryFilter(interaction =>
            AuthorizationFiltersBypassed || (CurrentUserId != null && interaction.UserId == CurrentUserId));

        modelBuilder.Entity<PlaybackSession>().HasQueryFilter(session =>
            AuthorizationFiltersBypassed || (CurrentUserId != null && session.UserId == CurrentUserId));

        modelBuilder.Entity<PlaybackInterval>().HasQueryFilter(interval =>
            AuthorizationFiltersBypassed || (CurrentUserId != null && interval.UserId == CurrentUserId));

        modelBuilder.Entity<Rating>().HasQueryFilter(rating =>
            AuthorizationFiltersBypassed || (CurrentUserId != null && rating.UserId == CurrentUserId));

        modelBuilder.Entity<SegmentDisplayProfile>().HasQueryFilter(profile =>
            AuthorizationFiltersBypassed || profile.UserId == null || (CurrentUserId != null && profile.UserId == CurrentUserId));

        modelBuilder.Entity<SegmentDisplayRule>().HasQueryFilter(rule =>
            AuthorizationFiltersBypassed || rule.UserId == null || (CurrentUserId != null && rule.UserId == CurrentUserId));

        modelBuilder.Entity<SceneMarker>().HasQueryFilter(marker =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresMarkerReadScopeEvaluation
                    ? CanReadSegments
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadSegments, CanReadSegmentsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Marker, marker.Id))
                && (!RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, marker.SceneId)));

        modelBuilder.Entity<GalleryChapter>().HasQueryFilter(chapter =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, chapter.GalleryId));

        modelBuilder.Entity<SceneUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, link.SceneId));

        modelBuilder.Entity<SceneRemoteId>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, link.SceneId));

        modelBuilder.Entity<ScenePlayHistory>().HasQueryFilter(entry =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, entry.SceneId));

        modelBuilder.Entity<SceneLikeHistory>().HasQueryFilter(entry =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, entry.SceneId));

        modelBuilder.Entity<PerformerUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId));

        modelBuilder.Entity<PerformerAlias>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId));

        modelBuilder.Entity<PerformerRemoteId>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId));

        modelBuilder.Entity<TagAlias>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId));

        modelBuilder.Entity<TagRemoteId>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId));

        modelBuilder.Entity<StudioUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresStudioReadScopeEvaluation
                    ? CanReadStudios
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadStudios, CanReadStudiosByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Studio, link.StudioId));

        modelBuilder.Entity<StudioAlias>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresStudioReadScopeEvaluation
                    ? CanReadStudios
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadStudios, CanReadStudiosByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Studio, link.StudioId));

        modelBuilder.Entity<StudioRemoteId>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresStudioReadScopeEvaluation
                    ? CanReadStudios
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadStudios, CanReadStudiosByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Studio, link.StudioId));

        modelBuilder.Entity<GalleryUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, link.GalleryId));

        modelBuilder.Entity<ImageUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresImageReadScopeEvaluation
                    ? CanReadImages
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadImages, CanReadImagesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Image, link.ImageId));

        modelBuilder.Entity<GroupUrl>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : !RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, link.GroupId));

        modelBuilder.Entity<SceneTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, link.SceneId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<ScenePerformer>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, link.SceneId))
                && (!RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId)));

        modelBuilder.Entity<SceneGallery>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, link.SceneId))
                && (!RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, link.GalleryId)));

        modelBuilder.Entity<GroupItem>().HasQueryFilter(item =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresSceneReadScopeEvaluation
                    ? CanReadScenes
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadScenes, CanReadScenesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Scene, item.SceneId))
                && (!RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, item.GroupId)));

        modelBuilder.Entity<PerformerTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<ImageTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresImageReadScopeEvaluation
                    ? CanReadImages
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadImages, CanReadImagesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Image, link.ImageId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<ImagePerformer>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresImageReadScopeEvaluation
                    ? CanReadImages
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadImages, CanReadImagesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Image, link.ImageId))
                && (!RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId)));

        modelBuilder.Entity<ImageGallery>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresImageReadScopeEvaluation
                    ? CanReadImages
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadImages, CanReadImagesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Image, link.ImageId))
                && (!RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, link.GalleryId)));

        modelBuilder.Entity<GalleryTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, link.GalleryId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<GalleryPerformer>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresGalleryReadScopeEvaluation
                    ? CanReadGalleries
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGalleries, CanReadGalleriesByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Gallery, link.GalleryId))
                && (!RequiresPerformerReadScopeEvaluation
                    ? CanReadPerformers
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadPerformers, CanReadPerformersByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Performer, link.PerformerId)));

        modelBuilder.Entity<StudioTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresStudioReadScopeEvaluation
                    ? CanReadStudios
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadStudios, CanReadStudiosByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Studio, link.StudioId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<GroupTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, link.GroupId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));

        modelBuilder.Entity<GroupRelation>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, link.ContainingGroupId))
                && (!RequiresGroupReadScopeEvaluation
                    ? CanReadGroups
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadGroups, CanReadGroupsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Group, link.SubGroupId)));

        modelBuilder.Entity<TagParent>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.ParentId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.ChildId)));

        modelBuilder.Entity<SceneMarkerTag>().HasQueryFilter(link =>
            AuthorizationFiltersBypassed
                ? true
                : (!RequiresMarkerReadScopeEvaluation
                    ? CanReadSegments
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadSegments, CanReadSegmentsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Marker, link.SceneMarkerId))
                && (!RequiresTagReadScopeEvaluation
                    ? CanReadTags
                    : CanReadEntitySql(AuthorizationFiltersBypassed, CanReadTags, CanReadTagsByRule, CurrentRoleNames, CurrentShareLinkId, EntityKinds.Tag, link.TagId)));
    }
}