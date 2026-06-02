using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CoveContext))]
    [Migration("20260602001000_SceneToVideoSchemaRename")]
    public partial class SceneToVideoSchemaRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RenameTable(migrationBuilder, "scenes", "videos");
            RenameTable(migrationBuilder, "scene_galleries", "video_galleries");
            RenameTable(migrationBuilder, "scene_performers", "video_performers");
            RenameTable(migrationBuilder, "scene_tags", "video_tags");
            RenameTable(migrationBuilder, "SceneLikeHistory", "VideoLikeHistory");
            RenameTable(migrationBuilder, "ScenePlayHistory", "VideoPlayHistory");
            RenameTable(migrationBuilder, "SceneRemoteId", "VideoRemoteId");
            RenameTable(migrationBuilder, "SceneUrl", "VideoUrl");
            RenameTable(migrationBuilder, "scene_markers", "video_markers");
            RenameTable(migrationBuilder, "scene_marker_tags", "video_marker_tags");

            RenameColumn(migrationBuilder, "videos", "ParentSceneId", "ParentVideoId");
            RenameColumn(migrationBuilder, "video_galleries", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "video_performers", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "video_tags", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "VideoLikeHistory", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "VideoPlayHistory", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "VideoRemoteId", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "VideoUrl", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "video_markers", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "video_marker_tags", "SceneMarkerId", "VideoMarkerId");
            RenameColumn(migrationBuilder, "files", "SceneId", "VideoId");
            RenameColumn(migrationBuilder, "group_items", "SceneId", "VideoId");

            RenameColumn(migrationBuilder, "performers", "SceneCount", "VideoCount");
            RenameColumn(migrationBuilder, "tags", "SceneCount", "VideoCount");
            RenameColumn(migrationBuilder, "tags", "SceneMarkerCount", "VideoMarkerCount");
            RenameColumn(migrationBuilder, "studios", "SceneCount", "VideoCount");
            RenameColumn(migrationBuilder, "galleries", "SceneCount", "VideoCount");
            RenameColumn(migrationBuilder, "faces", "SceneCount", "VideoCount");
            RenameColumn(migrationBuilder, "groups", "ShowInSceneLists", "ShowInVideoLists");

            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS public.group_items
                ALTER COLUMN "HostType" SET DEFAULT 'video';

                UPDATE public.group_items
                SET "HostType" = CASE "HostType"
                    WHEN 'scene' THEN 'video'
                    WHEN 'scene_range' THEN 'video_range'
                    ELSE "HostType"
                END
                WHERE "HostType" IN ('scene', 'scene_range');
                """);

            migrationBuilder.Sql("""
                INSERT INTO public.permissions ("Key", "Category", "Description", "Source", "Dangerous", "Implies", "IsOrphaned", "RegisteredAt")
                SELECT replace("Key", 'scenes.', 'videos.'),
                       replace("Category", 'Scenes', 'Videos'),
                       replace("Description", 'scenes', 'videos'),
                       "Source",
                       "Dangerous",
                       replace(coalesce("Implies"::text, '[]'), 'scenes.', 'videos.')::jsonb,
                       "IsOrphaned",
                       "RegisteredAt"
                FROM public.permissions
                WHERE "Key" LIKE 'scenes.%'
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO public.role_permissions ("RoleId", "PermissionKey")
                SELECT "RoleId", replace("PermissionKey", 'scenes.', 'videos.')
                FROM public.role_permissions
                WHERE "PermissionKey" LIKE 'scenes.%'
                ON CONFLICT DO NOTHING;

                DELETE FROM public.role_permissions WHERE "PermissionKey" LIKE 'scenes.%';
                DELETE FROM public.permissions WHERE "Key" LIKE 'scenes.%';

                UPDATE public.api_tokens
                SET "ScopePermissions" = replace(coalesce("ScopePermissions"::text, '[]'), 'scenes.', 'videos.')::jsonb
                WHERE "ScopePermissions"::text LIKE '%scenes.%';

                UPDATE public.role_content_rules SET "EntityKind" = 'video' WHERE "EntityKind" = 'scene';
                UPDATE public.role_entity_overrides SET "EntityKind" = 'video' WHERE "EntityKind" = 'scene';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO public.permissions ("Key", "Category", "Description", "Source", "Dangerous", "Implies", "IsOrphaned", "RegisteredAt")
                SELECT replace("Key", 'videos.', 'scenes.'),
                       replace("Category", 'Videos', 'Scenes'),
                       replace("Description", 'videos', 'scenes'),
                       "Source",
                       "Dangerous",
                       replace(coalesce("Implies"::text, '[]'), 'videos.', 'scenes.')::jsonb,
                       "IsOrphaned",
                       "RegisteredAt"
                FROM public.permissions
                WHERE "Key" LIKE 'videos.%'
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO public.role_permissions ("RoleId", "PermissionKey")
                SELECT "RoleId", replace("PermissionKey", 'videos.', 'scenes.')
                FROM public.role_permissions
                WHERE "PermissionKey" LIKE 'videos.%'
                ON CONFLICT DO NOTHING;

                DELETE FROM public.role_permissions WHERE "PermissionKey" LIKE 'videos.%';
                DELETE FROM public.permissions WHERE "Key" LIKE 'videos.%';

                UPDATE public.api_tokens
                SET "ScopePermissions" = replace(coalesce("ScopePermissions"::text, '[]'), 'videos.', 'scenes.')::jsonb
                WHERE "ScopePermissions"::text LIKE '%videos.%';

                UPDATE public.role_content_rules SET "EntityKind" = 'scene' WHERE "EntityKind" = 'video';
                UPDATE public.role_entity_overrides SET "EntityKind" = 'scene' WHERE "EntityKind" = 'video';
                """);

            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS public.group_items
                ALTER COLUMN "HostType" SET DEFAULT 'scene';

                UPDATE public.group_items
                SET "HostType" = CASE "HostType"
                    WHEN 'video' THEN 'scene'
                    WHEN 'video_range' THEN 'scene_range'
                    ELSE "HostType"
                END
                WHERE "HostType" IN ('video', 'video_range');
                """);

            RenameColumn(migrationBuilder, "groups", "ShowInVideoLists", "ShowInSceneLists");
            RenameColumn(migrationBuilder, "faces", "VideoCount", "SceneCount");
            RenameColumn(migrationBuilder, "galleries", "VideoCount", "SceneCount");
            RenameColumn(migrationBuilder, "studios", "VideoCount", "SceneCount");
            RenameColumn(migrationBuilder, "tags", "VideoMarkerCount", "SceneMarkerCount");
            RenameColumn(migrationBuilder, "tags", "VideoCount", "SceneCount");
            RenameColumn(migrationBuilder, "performers", "VideoCount", "SceneCount");

            RenameColumn(migrationBuilder, "group_items", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "files", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "video_marker_tags", "VideoMarkerId", "SceneMarkerId");
            RenameColumn(migrationBuilder, "video_markers", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "VideoUrl", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "VideoRemoteId", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "VideoPlayHistory", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "VideoLikeHistory", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "video_tags", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "video_performers", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "video_galleries", "VideoId", "SceneId");
            RenameColumn(migrationBuilder, "videos", "ParentVideoId", "ParentSceneId");

            RenameTable(migrationBuilder, "video_marker_tags", "scene_marker_tags");
            RenameTable(migrationBuilder, "video_markers", "scene_markers");
            RenameTable(migrationBuilder, "VideoUrl", "SceneUrl");
            RenameTable(migrationBuilder, "VideoRemoteId", "SceneRemoteId");
            RenameTable(migrationBuilder, "VideoPlayHistory", "ScenePlayHistory");
            RenameTable(migrationBuilder, "VideoLikeHistory", "SceneLikeHistory");
            RenameTable(migrationBuilder, "video_tags", "scene_tags");
            RenameTable(migrationBuilder, "video_performers", "scene_performers");
            RenameTable(migrationBuilder, "video_galleries", "scene_galleries");
            RenameTable(migrationBuilder, "videos", "scenes");
        }

        private static void RenameTable(MigrationBuilder migrationBuilder, string oldName, string newName)
        {
            migrationBuilder.Sql($$"""
                DO $$
                BEGIN
                    IF to_regclass('public."{{oldName}}"') IS NOT NULL
                       AND to_regclass('public."{{newName}}"') IS NULL THEN
                        ALTER TABLE public."{{oldName}}" RENAME TO "{{newName}}";
                    END IF;
                END $$;
                """);
        }

        private static void RenameColumn(MigrationBuilder migrationBuilder, string tableName, string oldName, string newName)
        {
            migrationBuilder.Sql($$"""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = '{{tableName}}'
                          AND column_name = '{{oldName}}'
                    ) AND NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = '{{tableName}}'
                          AND column_name = '{{newName}}'
                    ) THEN
                        ALTER TABLE public."{{tableName}}" RENAME COLUMN "{{oldName}}" TO "{{newName}}";
                    END IF;
                END $$;
                """);
        }
    }
}
