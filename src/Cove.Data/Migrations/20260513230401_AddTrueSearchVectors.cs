using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrueSearchVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "text_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "tags",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "studios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "performers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "images",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "groups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "galleries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "faces",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "audios",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "text_documents",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "tags",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"SortName\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Description\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "studios",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "scenes",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Director\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Captions\", '') || ' ' || coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "performers",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Disambiguation\", '') || ' ' || coalesce(\"Details\", '') || ' ' || coalesce(\"SearchText\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Country\", '') || ' ' || coalesce(\"Ethnicity\", '') || ' ' || coalesce(\"Tattoos\", '') || ' ' || coalesce(\"Piercings\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "images",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Photographer\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "groups",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Aliases\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Synopsis\", '') || ' ' || coalesce(\"Director\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "galleries",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Photographer\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"SearchText\", '')), 'C')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "faces",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Label\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"PrimarySourceKey\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "audios",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')",
                stored: true);

            migrationBuilder.Sql(TrueSearchFunctionsSql);

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_SearchVector",
                table: "text_documents",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SearchVector",
                table: "tags",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_studios_SearchVector",
                table: "studios",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_SearchVector",
                table: "scenes",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_performers_SearchVector",
                table: "performers",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_SearchVector",
                table: "images",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_groups_SearchVector",
                table: "groups",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_SearchVector",
                table: "galleries",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_faces_SearchVector",
                table: "faces",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_audios_SearchVector",
                table: "audios",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropTrueSearchFunctionsSql);

            migrationBuilder.DropIndex(
                name: "IX_text_documents_SearchVector",
                table: "text_documents");

            migrationBuilder.DropIndex(
                name: "IX_tags_SearchVector",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_studios_SearchVector",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_scenes_SearchVector",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_performers_SearchVector",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_images_SearchVector",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_groups_SearchVector",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_galleries_SearchVector",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_faces_SearchVector",
                table: "faces");

            migrationBuilder.DropIndex(
                name: "IX_audios_SearchVector",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "text_documents");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "images");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "faces");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "audios");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "text_documents");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "images");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "faces");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "audios");
        }

        private const string TrueSearchFunctionsSql = """
CREATE OR REPLACE FUNCTION cove_search_clean(search_text text)
RETURNS text
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT NULLIF(regexp_replace(trim(COALESCE(search_text, '')), '[[:space:]]+', ' ', 'g'), '');
$$;

CREATE OR REPLACE FUNCTION cove_search_custom_fields(search_entity_type text, search_entity_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(string_agg(concat_ws(' ',
        definition."Key",
        definition."Label",
        custom_value."TextValue",
        custom_value."NumberValue"::text,
        custom_value."IntegerValue"::text,
        custom_value."BoolValue"::text,
        custom_value."DateValue"::text,
        custom_value."TimestampValue"::text
    ), ' '), '')
    FROM custom_field_values custom_value
    JOIN custom_field_definitions definition ON definition."Id" = custom_value."DefinitionId"
    WHERE custom_value."EntityType" = search_entity_type
      AND custom_value."EntityId" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_search_entity_identifiers(search_entity_kind text, search_entity_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(string_agg(concat_ws(' ', identifier."Scheme", identifier."Source", identifier."Value", identifier."NormalizedValue"), ' '), '')
    FROM entity_identifiers identifier
    WHERE identifier."EntityKind" = search_entity_kind
      AND identifier."EntityId" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_search_performer_aliases(search_performer_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(string_agg(alias."Alias", ' '), '')
    FROM "PerformerAlias" alias
    WHERE alias."PerformerId" = search_performer_id;
$$;

CREATE OR REPLACE FUNCTION cove_search_tag_aliases(search_tag_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(string_agg(alias."Alias", ' '), '')
    FROM "TagAlias" alias
    WHERE alias."TagId" = search_tag_id;
$$;

CREATE OR REPLACE FUNCTION cove_search_studio_aliases(search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(string_agg(alias."Alias", ' '), '')
    FROM "StudioAlias" alias
    WHERE alias."StudioId" = search_studio_id;
$$;

CREATE OR REPLACE FUNCTION cove_search_scene_text(search_entity_id integer, search_studio_id integer, search_parent_scene_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT concat_ws(' ', parent_scene."Title", parent_scene."Code") FROM scenes parent_scene WHERE parent_scene."Id" = search_parent_scene_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM scene_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."SceneId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")), ' '), '')
            FROM scene_performers link
            JOIN performers performer ON performer."Id" = link."PerformerId"
            WHERE link."SceneId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', gallery."Title", gallery."Code", gallery."Details"), ' '), '')
            FROM scene_galleries link
            JOIN galleries gallery ON gallery."Id" = link."GalleryId"
            WHERE link."SceneId" = search_entity_id),
        (SELECT COALESCE(string_agg(scene_url."Url", ' '), '') FROM "SceneUrl" scene_url WHERE scene_url."SceneId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', remote."Endpoint", remote."RemoteId"), ' '), '') FROM "SceneRemoteId" remote WHERE remote."SceneId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', scene_group."Name", scene_group."Aliases"), ' '), '')
            FROM group_items item
            JOIN groups scene_group ON scene_group."Id" = item."GroupId"
            WHERE (item."HostType" = 'scene' AND item."HostId" = search_entity_id) OR item."SceneId" = search_entity_id),
        cove_search_entity_identifiers('scene', search_entity_id),
        cove_search_custom_fields('scene', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_image_text(search_entity_id integer, search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM image_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."ImageId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")), ' '), '')
            FROM image_performers link
            JOIN performers performer ON performer."Id" = link."PerformerId"
            WHERE link."ImageId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', gallery."Title", gallery."Code", gallery."Details"), ' '), '')
            FROM image_galleries link
            JOIN galleries gallery ON gallery."Id" = link."GalleryId"
            WHERE link."ImageId" = search_entity_id),
        (SELECT COALESCE(string_agg(image_url."Url", ' '), '') FROM "ImageUrl" image_url WHERE image_url."ImageId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', image_group."Name", image_group."Aliases"), ' '), '')
            FROM group_items item
            JOIN groups image_group ON image_group."Id" = item."GroupId"
            WHERE (item."HostType" = 'image' AND item."HostId" = search_entity_id) OR item."ImageId" = search_entity_id),
        cove_search_entity_identifiers('image', search_entity_id),
        cove_search_custom_fields('image', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_audio_text(search_entity_id integer, search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM audio_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."AudioId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")), ' '), '')
            FROM audio_performers link
            JOIN performers performer ON performer."Id" = link."PerformerId"
            WHERE link."AudioId" = search_entity_id),
        (SELECT COALESCE(string_agg(track."Title", ' '), '') FROM audio_tracks track WHERE track."AudioId" = search_entity_id),
        (SELECT COALESCE(string_agg(audio_url."Url", ' '), '') FROM audio_urls audio_url WHERE audio_url."AudioId" = search_entity_id),
        cove_search_entity_identifiers('audio', search_entity_id),
        cove_search_custom_fields('audio', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_text_document_text(search_entity_id integer, search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM text_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."TextDocumentId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")), ' '), '')
            FROM text_performers link
            JOIN performers performer ON performer."Id" = link."PerformerId"
            WHERE link."TextDocumentId" = search_entity_id),
        (SELECT COALESCE(string_agg(text_url."Url", ' '), '') FROM text_urls text_url WHERE text_url."TextDocumentId" = search_entity_id),
        cove_search_entity_identifiers('text', search_entity_id),
        cove_search_custom_fields('text', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_performer_text(search_entity_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', performer."EyeColor", performer."HairColor", performer."Measurements", performer."FakeTits") FROM performers performer WHERE performer."Id" = search_entity_id),
        cove_search_performer_aliases(search_entity_id),
        (SELECT COALESCE(string_agg(performer_url."Url", ' '), '') FROM "PerformerUrl" performer_url WHERE performer_url."PerformerId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', remote."Endpoint", remote."RemoteId"), ' '), '') FROM "PerformerRemoteId" remote WHERE remote."PerformerId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM performer_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."PerformerId" = search_entity_id),
        cove_search_entity_identifiers('performer', search_entity_id),
        cove_search_custom_fields('performer', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_tag_text(search_entity_id integer, search_tag_group_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', tag_group."Name", tag_group."Description") FROM tag_groups tag_group WHERE tag_group."Id" = search_tag_group_id),
        cove_search_tag_aliases(search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', remote."Endpoint", remote."RemoteId"), ' '), '') FROM "TagRemoteId" remote WHERE remote."TagId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', parent_tag."Name", parent_tag."SortName"), ' '), '')
            FROM tag_parents link
            JOIN tags parent_tag ON parent_tag."Id" = link."ParentId"
            WHERE link."ChildId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', child_tag."Name", child_tag."SortName"), ' '), '')
            FROM tag_parents link
            JOIN tags child_tag ON child_tag."Id" = link."ChildId"
            WHERE link."ParentId" = search_entity_id),
        cove_search_entity_identifiers('tag', search_entity_id),
        cove_search_custom_fields('tag', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_studio_text(search_entity_id integer, search_parent_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', parent_studio."Name", parent_studio."Details", cove_search_studio_aliases(parent_studio."Id")) FROM studios parent_studio WHERE parent_studio."Id" = search_parent_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', child_studio."Name", child_studio."Details", cove_search_studio_aliases(child_studio."Id")), ' '), '') FROM studios child_studio WHERE child_studio."ParentId" = search_entity_id),
        cove_search_studio_aliases(search_entity_id),
        (SELECT COALESCE(string_agg(studio_url."Url", ' '), '') FROM "StudioUrl" studio_url WHERE studio_url."StudioId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', remote."Endpoint", remote."RemoteId"), ' '), '') FROM "StudioRemoteId" remote WHERE remote."StudioId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM studio_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."StudioId" = search_entity_id),
        cove_search_entity_identifiers('studio', search_entity_id),
        cove_search_custom_fields('studio', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_gallery_text(search_entity_id integer, search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM gallery_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."GalleryId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")), ' '), '')
            FROM gallery_performers link
            JOIN performers performer ON performer."Id" = link."PerformerId"
            WHERE link."GalleryId" = search_entity_id),
        (SELECT COALESCE(string_agg(gallery_url."Url", ' '), '') FROM "GalleryUrl" gallery_url WHERE gallery_url."GalleryId" = search_entity_id),
        (SELECT COALESCE(string_agg(chapter."Title", ' '), '') FROM gallery_chapters chapter WHERE chapter."GalleryId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', file."Basename", file."Path"), ' '), '') FROM files file WHERE file."GalleryId" = search_entity_id),
        cove_search_entity_identifiers('gallery', search_entity_id),
        cove_search_custom_fields('gallery', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_group_text(search_entity_id integer, search_studio_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', studio."Name", studio."Details", cove_search_studio_aliases(studio."Id")) FROM studios studio WHERE studio."Id" = search_studio_id),
        (SELECT COALESCE(string_agg(group_url."Url", ' '), '') FROM "GroupUrl" group_url WHERE group_url."GroupId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', tag."Name", tag."SortName", tag."Description", tag_group."Name", cove_search_tag_aliases(tag."Id")), ' '), '')
            FROM group_tags link
            JOIN tags tag ON tag."Id" = link."TagId"
            LEFT JOIN tag_groups tag_group ON tag_group."Id" = tag."TagGroupId"
            WHERE link."GroupId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', item."Title", item."Notes", item."SourceSpanKey", scene."Title", image."Title", child_group."Name"), ' '), '')
            FROM group_items item
            LEFT JOIN scenes scene ON scene."Id" = item."SceneId"
            LEFT JOIN images image ON image."Id" = item."ImageId"
            LEFT JOIN groups child_group ON child_group."Id" = item."ChildGroupId"
            WHERE item."GroupId" = search_entity_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', related_group."Name", relation."Description"), ' '), '')
            FROM group_relations relation
            JOIN groups related_group ON related_group."Id" = relation."SubGroupId"
            WHERE relation."ContainingGroupId" = search_entity_id),
        cove_search_entity_identifiers('group', search_entity_id),
        cove_search_custom_fields('group', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_search_face_text(search_entity_id integer, search_performer_id integer)
RETURNS text
LANGUAGE sql
STABLE
AS $$
    SELECT concat_ws(' ',
        (SELECT concat_ws(' ', performer."Name", performer."Disambiguation", performer."Details", cove_search_performer_aliases(performer."Id")) FROM performers performer WHERE performer."Id" = search_performer_id),
        (SELECT COALESCE(string_agg(concat_ws(' ', appearance."SourceKey", appearance."SourceRunId", appearance."GroupKey"), ' '), '') FROM face_appearances appearance WHERE appearance."FaceId" = search_entity_id),
        cove_search_entity_identifiers('face', search_entity_id),
        cove_search_custom_fields('face', search_entity_id)
    );
$$;

CREATE OR REPLACE FUNCTION cove_refresh_scene_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE scenes SET "SearchText" = cove_search_clean(cove_search_scene_text("Id", "StudioId", "ParentSceneId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_image_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE images SET "SearchText" = cove_search_clean(cove_search_image_text("Id", "StudioId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_audio_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE audios SET "SearchText" = cove_search_clean(cove_search_audio_text("Id", "StudioId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_text_document_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE text_documents SET "SearchText" = cove_search_clean(cove_search_text_document_text("Id", "StudioId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_performer_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE performers SET "SearchText" = cove_search_clean(cove_search_performer_text("Id")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_tag_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE tags SET "SearchText" = cove_search_clean(cove_search_tag_text("Id", "TagGroupId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_studio_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE studios SET "SearchText" = cove_search_clean(cove_search_studio_text("Id", "ParentId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_gallery_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE galleries SET "SearchText" = cove_search_clean(cove_search_gallery_text("Id", "StudioId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_group_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE groups SET "SearchText" = cove_search_clean(cove_search_group_text("Id", "StudioId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_face_search_text(search_entity_id integer) RETURNS void LANGUAGE sql AS $$
    UPDATE faces SET "SearchText" = cove_search_clean(cove_search_face_text("Id", "PerformerId")) WHERE "Id" = search_entity_id;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_search_text_by_kind(search_entity_kind text, search_entity_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    IF search_entity_id IS NULL THEN
        RETURN;
    END IF;

    CASE search_entity_kind
        WHEN 'scene' THEN PERFORM cove_refresh_scene_search_text(search_entity_id);
        WHEN 'image' THEN PERFORM cove_refresh_image_search_text(search_entity_id);
        WHEN 'audio' THEN PERFORM cove_refresh_audio_search_text(search_entity_id);
        WHEN 'text' THEN PERFORM cove_refresh_text_document_search_text(search_entity_id);
        WHEN 'performer' THEN PERFORM cove_refresh_performer_search_text(search_entity_id);
        WHEN 'tag' THEN PERFORM cove_refresh_tag_search_text(search_entity_id);
        WHEN 'studio' THEN PERFORM cove_refresh_studio_search_text(search_entity_id);
        WHEN 'gallery' THEN PERFORM cove_refresh_gallery_search_text(search_entity_id);
        WHEN 'group' THEN PERFORM cove_refresh_group_search_text(search_entity_id);
        WHEN 'face' THEN PERFORM cove_refresh_face_search_text(search_entity_id);
        ELSE RETURN;
    END CASE;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_tag_dependents(search_tag_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    PERFORM cove_refresh_tag_search_text(search_tag_id);

    FOR related_id IN SELECT link."SceneId" FROM scene_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_scene_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."ImageId" FROM image_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_image_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."GalleryId" FROM gallery_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_gallery_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."AudioId" FROM audio_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_audio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."TextDocumentId" FROM text_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_text_document_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."PerformerId" FROM performer_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_performer_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."StudioId" FROM studio_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_studio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."GroupId" FROM group_tags link WHERE link."TagId" = search_tag_id LOOP PERFORM cove_refresh_group_search_text(related_id); END LOOP;
    FOR related_id IN
        SELECT link."ParentId" FROM tag_parents link WHERE link."ChildId" = search_tag_id
        UNION
        SELECT link."ChildId" FROM tag_parents link WHERE link."ParentId" = search_tag_id
    LOOP
        PERFORM cove_refresh_tag_search_text(related_id);
    END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_performer_dependents(search_performer_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    PERFORM cove_refresh_performer_search_text(search_performer_id);

    FOR related_id IN SELECT link."SceneId" FROM scene_performers link WHERE link."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_scene_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."ImageId" FROM image_performers link WHERE link."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_image_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."GalleryId" FROM gallery_performers link WHERE link."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_gallery_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."AudioId" FROM audio_performers link WHERE link."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_audio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."TextDocumentId" FROM text_performers link WHERE link."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_text_document_search_text(related_id); END LOOP;
    FOR related_id IN SELECT face."Id" FROM faces face WHERE face."PerformerId" = search_performer_id LOOP PERFORM cove_refresh_face_search_text(related_id); END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_studio_dependents(search_studio_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    PERFORM cove_refresh_studio_search_text(search_studio_id);

    FOR related_id IN SELECT studio."Id" FROM studios studio WHERE studio."ParentId" = search_studio_id LOOP PERFORM cove_refresh_studio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT studio."ParentId" FROM studios studio WHERE studio."Id" = search_studio_id AND studio."ParentId" IS NOT NULL LOOP PERFORM cove_refresh_studio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT scene."Id" FROM scenes scene WHERE scene."StudioId" = search_studio_id LOOP PERFORM cove_refresh_scene_search_text(related_id); END LOOP;
    FOR related_id IN SELECT image."Id" FROM images image WHERE image."StudioId" = search_studio_id LOOP PERFORM cove_refresh_image_search_text(related_id); END LOOP;
    FOR related_id IN SELECT gallery."Id" FROM galleries gallery WHERE gallery."StudioId" = search_studio_id LOOP PERFORM cove_refresh_gallery_search_text(related_id); END LOOP;
    FOR related_id IN SELECT audio."Id" FROM audios audio WHERE audio."StudioId" = search_studio_id LOOP PERFORM cove_refresh_audio_search_text(related_id); END LOOP;
    FOR related_id IN SELECT text_document."Id" FROM text_documents text_document WHERE text_document."StudioId" = search_studio_id LOOP PERFORM cove_refresh_text_document_search_text(related_id); END LOOP;
    FOR related_id IN SELECT media_group."Id" FROM groups media_group WHERE media_group."StudioId" = search_studio_id LOOP PERFORM cove_refresh_group_search_text(related_id); END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_gallery_dependents(search_gallery_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    PERFORM cove_refresh_gallery_search_text(search_gallery_id);

    FOR related_id IN SELECT link."SceneId" FROM scene_galleries link WHERE link."GalleryId" = search_gallery_id LOOP PERFORM cove_refresh_scene_search_text(related_id); END LOOP;
    FOR related_id IN SELECT link."ImageId" FROM image_galleries link WHERE link."GalleryId" = search_gallery_id LOOP PERFORM cove_refresh_image_search_text(related_id); END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_group_dependents(search_group_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    PERFORM cove_refresh_group_search_text(search_group_id);

    FOR related_id IN
        SELECT relation."ContainingGroupId" FROM group_relations relation WHERE relation."SubGroupId" = search_group_id
        UNION
        SELECT relation."SubGroupId" FROM group_relations relation WHERE relation."ContainingGroupId" = search_group_id
        UNION
        SELECT item."GroupId" FROM group_items item WHERE item."ChildGroupId" = search_group_id
    LOOP
        PERFORM cove_refresh_group_search_text(related_id);
    END LOOP;

    FOR related_id IN SELECT item."HostId" FROM group_items item WHERE item."GroupId" = search_group_id AND item."HostType" = 'scene' LOOP PERFORM cove_refresh_scene_search_text(related_id); END LOOP;
    FOR related_id IN SELECT item."HostId" FROM group_items item WHERE item."GroupId" = search_group_id AND item."HostType" = 'image' LOOP PERFORM cove_refresh_image_search_text(related_id); END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_search_text_dependents(search_entity_kind text, search_entity_id integer)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    IF search_entity_id IS NULL THEN
        RETURN;
    END IF;

    CASE search_entity_kind
        WHEN 'tag' THEN PERFORM cove_refresh_tag_dependents(search_entity_id);
        WHEN 'performer' THEN PERFORM cove_refresh_performer_dependents(search_entity_id);
        WHEN 'studio' THEN PERFORM cove_refresh_studio_dependents(search_entity_id);
        WHEN 'gallery' THEN PERFORM cove_refresh_gallery_dependents(search_entity_id);
        WHEN 'group' THEN PERFORM cove_refresh_group_dependents(search_entity_id);
        ELSE PERFORM cove_refresh_search_text_by_kind(search_entity_kind, search_entity_id);
    END CASE;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_search_text_link_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    entity_kind text := TG_ARGV[0];
    id_column text := TG_ARGV[1];
    old_id integer;
    new_id integer;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        old_id := NULLIF(to_jsonb(OLD) ->> id_column, '')::integer;
        PERFORM cove_refresh_search_text_by_kind(entity_kind, old_id);
    END IF;

    IF TG_OP <> 'DELETE' THEN
        new_id := NULLIF(to_jsonb(NEW) ->> id_column, '')::integer;
        IF new_id IS DISTINCT FROM old_id THEN
            PERFORM cove_refresh_search_text_by_kind(entity_kind, new_id);
        END IF;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_search_text_dependents_link_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    entity_kind text := TG_ARGV[0];
    id_column text := TG_ARGV[1];
    old_id integer;
    new_id integer;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        old_id := NULLIF(to_jsonb(OLD) ->> id_column, '')::integer;
        PERFORM cove_refresh_search_text_dependents(entity_kind, old_id);
    END IF;

    IF TG_OP <> 'DELETE' THEN
        new_id := NULLIF(to_jsonb(NEW) ->> id_column, '')::integer;
        IF new_id IS DISTINCT FROM old_id THEN
            PERFORM cove_refresh_search_text_dependents(entity_kind, new_id);
        END IF;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_search_text_polymorphic_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    kind_column text := TG_ARGV[0];
    id_column text := TG_ARGV[1];
    old_kind text;
    new_kind text;
    old_id integer;
    new_id integer;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        old_kind := to_jsonb(OLD) ->> kind_column;
        old_id := NULLIF(to_jsonb(OLD) ->> id_column, '')::integer;
        PERFORM cove_refresh_search_text_by_kind(old_kind, old_id);
    END IF;

    IF TG_OP <> 'DELETE' THEN
        new_kind := to_jsonb(NEW) ->> kind_column;
        new_id := NULLIF(to_jsonb(NEW) ->> id_column, '')::integer;
        IF new_kind IS DISTINCT FROM old_kind OR new_id IS DISTINCT FROM old_id THEN
            PERFORM cove_refresh_search_text_by_kind(new_kind, new_id);
        END IF;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_group_item_search_text_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    row_data jsonb;
    host_type text;
    host_id integer;
    scene_id integer;
    image_id integer;
    child_group_id integer;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        row_data := to_jsonb(OLD);
        PERFORM cove_refresh_search_text_by_kind('group', NULLIF(row_data ->> 'GroupId', '')::integer);
        host_type := row_data ->> 'HostType';
        host_id := NULLIF(row_data ->> 'HostId', '')::integer;
        scene_id := NULLIF(row_data ->> 'SceneId', '')::integer;
        image_id := NULLIF(row_data ->> 'ImageId', '')::integer;
        child_group_id := NULLIF(row_data ->> 'ChildGroupId', '')::integer;

        IF host_type IN ('scene', 'image', 'audio', 'text', 'performer', 'studio', 'tag', 'gallery', 'face', 'group') THEN
            PERFORM cove_refresh_search_text_by_kind(host_type, host_id);
        END IF;
        PERFORM cove_refresh_scene_search_text(scene_id);
        PERFORM cove_refresh_image_search_text(image_id);
        PERFORM cove_refresh_group_search_text(child_group_id);
    END IF;

    IF TG_OP <> 'DELETE' THEN
        row_data := to_jsonb(NEW);
        PERFORM cove_refresh_search_text_by_kind('group', NULLIF(row_data ->> 'GroupId', '')::integer);
        host_type := row_data ->> 'HostType';
        host_id := NULLIF(row_data ->> 'HostId', '')::integer;
        scene_id := NULLIF(row_data ->> 'SceneId', '')::integer;
        image_id := NULLIF(row_data ->> 'ImageId', '')::integer;
        child_group_id := NULLIF(row_data ->> 'ChildGroupId', '')::integer;

        IF host_type IN ('scene', 'image', 'audio', 'text', 'performer', 'studio', 'tag', 'gallery', 'face', 'group') THEN
            PERFORM cove_refresh_search_text_by_kind(host_type, host_id);
        END IF;
        PERFORM cove_refresh_scene_search_text(scene_id);
        PERFORM cove_refresh_image_search_text(image_id);
        PERFORM cove_refresh_group_search_text(child_group_id);
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION cove_refresh_tag_group_search_text_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE related_id integer;
BEGIN
    IF TG_OP <> 'INSERT' THEN
        FOR related_id IN SELECT tag."Id" FROM tags tag WHERE tag."TagGroupId" = OLD."Id" LOOP PERFORM cove_refresh_tag_dependents(related_id); END LOOP;
    END IF;

    IF TG_OP <> 'DELETE' THEN
        FOR related_id IN SELECT tag."Id" FROM tags tag WHERE tag."TagGroupId" = NEW."Id" LOOP PERFORM cove_refresh_tag_dependents(related_id); END LOOP;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION cove_create_search_trigger(
    search_trigger_name text,
    search_relation_name text,
    search_trigger_events text,
    search_trigger_function text,
    VARIADIC search_trigger_args text[])
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    search_relation_oid oid;
    search_relation_kind text;
    search_args_sql text;
BEGIN
    SELECT c.oid, c.relkind::text
    INTO search_relation_oid, search_relation_kind
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relname = search_relation_name
    LIMIT 1;

    IF search_relation_oid IS NULL OR search_relation_kind NOT IN ('r', 'p') THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_trigger t
        WHERE t.tgrelid = search_relation_oid AND t.tgname = search_trigger_name AND NOT t.tgisinternal
    ) THEN
        RETURN;
    END IF;

    SELECT COALESCE(string_agg(quote_literal(arg), ', '), '')
    INTO search_args_sql
    FROM unnest(search_trigger_args) AS arg;

    EXECUTE format(
        'CREATE TRIGGER %I %s ON %I FOR EACH ROW EXECUTE FUNCTION %I(%s)',
        search_trigger_name,
        search_trigger_events,
        search_relation_name,
        search_trigger_function,
        search_args_sql);
END;
$$;

CREATE OR REPLACE FUNCTION cove_create_search_trigger(
    search_trigger_name text,
    search_relation_name text,
    search_trigger_events text,
    search_trigger_function text)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM cove_create_search_trigger(
        search_trigger_name,
        search_relation_name,
        search_trigger_events,
        search_trigger_function,
        VARIADIC ARRAY[]::text[]);
END;
$$;

SELECT cove_create_search_trigger('cove_search_scene_rows', 'scenes', 'AFTER INSERT OR UPDATE OF "StudioId", "ParentSceneId"', 'cove_refresh_search_text_link_trigger', 'scene', 'Id');
SELECT cove_create_search_trigger('cove_search_image_rows', 'images', 'AFTER INSERT OR UPDATE OF "StudioId"', 'cove_refresh_search_text_link_trigger', 'image', 'Id');
SELECT cove_create_search_trigger('cove_search_audio_rows', 'audios', 'AFTER INSERT OR UPDATE OF "StudioId"', 'cove_refresh_search_text_link_trigger', 'audio', 'Id');
SELECT cove_create_search_trigger('cove_search_text_rows', 'text_documents', 'AFTER INSERT OR UPDATE OF "StudioId"', 'cove_refresh_search_text_link_trigger', 'text', 'Id');
SELECT cove_create_search_trigger('cove_search_face_rows', 'faces', 'AFTER INSERT OR UPDATE OF "PerformerId"', 'cove_refresh_search_text_link_trigger', 'face', 'Id');

SELECT cove_create_search_trigger('cove_search_tag_rows', 'tags', 'AFTER INSERT OR UPDATE OF "Name", "SortName", "Description", "TagGroupId"', 'cove_refresh_search_text_dependents_link_trigger', 'tag', 'Id');
SELECT cove_create_search_trigger('cove_search_performer_rows', 'performers', 'AFTER INSERT OR UPDATE OF "Name", "Disambiguation", "Details", "Country", "Ethnicity", "Tattoos", "Piercings", "EyeColor", "HairColor", "Measurements", "FakeTits"', 'cove_refresh_search_text_dependents_link_trigger', 'performer', 'Id');
SELECT cove_create_search_trigger('cove_search_studio_rows', 'studios', 'AFTER INSERT OR UPDATE OF "Name", "Details", "ParentId"', 'cove_refresh_search_text_dependents_link_trigger', 'studio', 'Id');
SELECT cove_create_search_trigger('cove_search_gallery_rows', 'galleries', 'AFTER INSERT OR UPDATE OF "Title", "Code", "Details", "Photographer", "StudioId"', 'cove_refresh_search_text_dependents_link_trigger', 'gallery', 'Id');
SELECT cove_create_search_trigger('cove_search_group_rows', 'groups', 'AFTER INSERT OR UPDATE OF "Name", "Aliases", "Synopsis", "Director", "StudioId"', 'cove_refresh_search_text_dependents_link_trigger', 'group', 'Id');
SELECT cove_create_search_trigger('cove_search_tag_group_rows', 'tag_groups', 'AFTER INSERT OR UPDATE OF "Name", "Description" OR DELETE', 'cove_refresh_tag_group_search_text_trigger');

SELECT cove_create_search_trigger('cove_search_scene_tags', 'scene_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'scene', 'SceneId');
SELECT cove_create_search_trigger('cove_search_scene_performers', 'scene_performers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'scene', 'SceneId');
SELECT cove_create_search_trigger('cove_search_scene_galleries', 'scene_galleries', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'scene', 'SceneId');
SELECT cove_create_search_trigger('cove_search_image_tags', 'image_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'image', 'ImageId');
SELECT cove_create_search_trigger('cove_search_image_performers', 'image_performers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'image', 'ImageId');
SELECT cove_create_search_trigger('cove_search_image_galleries', 'image_galleries', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'image', 'ImageId');
SELECT cove_create_search_trigger('cove_search_gallery_tags', 'gallery_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'gallery', 'GalleryId');
SELECT cove_create_search_trigger('cove_search_gallery_performers', 'gallery_performers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'gallery', 'GalleryId');
SELECT cove_create_search_trigger('cove_search_audio_tags', 'audio_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'audio', 'AudioId');
SELECT cove_create_search_trigger('cove_search_audio_performers', 'audio_performers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'audio', 'AudioId');
SELECT cove_create_search_trigger('cove_search_text_tags', 'text_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'text', 'TextDocumentId');
SELECT cove_create_search_trigger('cove_search_text_performers', 'text_performers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'text', 'TextDocumentId');
SELECT cove_create_search_trigger('cove_search_performer_tags', 'performer_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'performer', 'PerformerId');
SELECT cove_create_search_trigger('cove_search_studio_tags', 'studio_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'studio', 'StudioId');
SELECT cove_create_search_trigger('cove_search_group_tags', 'group_tags', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'group', 'GroupId');
SELECT cove_create_search_trigger('cove_search_group_items', 'group_items', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_group_item_search_text_trigger');
SELECT cove_create_search_trigger('cove_search_group_relations', 'group_relations', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_dependents_link_trigger', 'group', 'ContainingGroupId');

SELECT cove_create_search_trigger('cove_search_scene_urls', 'SceneUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'scene', 'SceneId');
SELECT cove_create_search_trigger('cove_search_image_urls', 'ImageUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'image', 'ImageId');
SELECT cove_create_search_trigger('cove_search_gallery_urls', 'GalleryUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'gallery', 'GalleryId');
SELECT cove_create_search_trigger('cove_search_group_urls', 'GroupUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'group', 'GroupId');
SELECT cove_create_search_trigger('cove_search_audio_urls', 'audio_urls', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'audio', 'AudioId');
SELECT cove_create_search_trigger('cove_search_text_urls', 'text_urls', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'text', 'TextDocumentId');

SELECT cove_create_search_trigger('cove_search_performer_aliases', 'PerformerAlias', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_dependents_link_trigger', 'performer', 'PerformerId');
SELECT cove_create_search_trigger('cove_search_tag_aliases', 'TagAlias', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_dependents_link_trigger', 'tag', 'TagId');
SELECT cove_create_search_trigger('cove_search_studio_aliases', 'StudioAlias', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_dependents_link_trigger', 'studio', 'StudioId');
SELECT cove_create_search_trigger('cove_search_performer_urls', 'PerformerUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'performer', 'PerformerId');
SELECT cove_create_search_trigger('cove_search_studio_urls', 'StudioUrl', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'studio', 'StudioId');
SELECT cove_create_search_trigger('cove_search_scene_remote_ids', 'SceneRemoteId', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'scene', 'SceneId');
SELECT cove_create_search_trigger('cove_search_performer_remote_ids', 'PerformerRemoteId', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'performer', 'PerformerId');
SELECT cove_create_search_trigger('cove_search_tag_remote_ids', 'TagRemoteId', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'tag', 'TagId');
SELECT cove_create_search_trigger('cove_search_studio_remote_ids', 'StudioRemoteId', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_link_trigger', 'studio', 'StudioId');

SELECT cove_create_search_trigger('cove_search_custom_field_values', 'custom_field_values', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_polymorphic_trigger', 'EntityType', 'EntityId');
SELECT cove_create_search_trigger('cove_search_entity_identifiers', 'entity_identifiers', 'AFTER INSERT OR UPDATE OR DELETE', 'cove_refresh_search_text_polymorphic_trigger', 'EntityKind', 'EntityId');
SELECT cove_create_search_trigger('cove_search_gallery_files', 'files', 'AFTER INSERT OR UPDATE OF "GalleryId", "Basename", "Path" OR DELETE', 'cove_refresh_search_text_link_trigger', 'gallery', 'GalleryId');

SELECT cove_refresh_scene_search_text("Id") FROM scenes;
SELECT cove_refresh_image_search_text("Id") FROM images;
SELECT cove_refresh_audio_search_text("Id") FROM audios;
SELECT cove_refresh_text_document_search_text("Id") FROM text_documents;
SELECT cove_refresh_performer_search_text("Id") FROM performers;
SELECT cove_refresh_tag_search_text("Id") FROM tags;
SELECT cove_refresh_studio_search_text("Id") FROM studios;
SELECT cove_refresh_gallery_search_text("Id") FROM galleries;
SELECT cove_refresh_group_search_text("Id") FROM groups;
SELECT cove_refresh_face_search_text("Id") FROM faces;
""";

        private const string DropTrueSearchFunctionsSql = """
    DROP FUNCTION IF EXISTS cove_create_search_trigger(text, text, text, text) CASCADE;
    DROP FUNCTION IF EXISTS cove_create_search_trigger(text, text, text, text, text[]) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_group_item_search_text_trigger() CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_tag_group_search_text_trigger() CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_search_text_polymorphic_trigger() CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_search_text_dependents_link_trigger() CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_search_text_link_trigger() CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_search_text_dependents(text, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_group_dependents(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_gallery_dependents(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_studio_dependents(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_performer_dependents(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_tag_dependents(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_search_text_by_kind(text, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_face_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_group_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_gallery_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_studio_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_tag_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_performer_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_text_document_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_audio_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_image_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_refresh_scene_search_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_face_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_group_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_gallery_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_studio_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_tag_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_performer_text(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_text_document_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_audio_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_image_text(integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_scene_text(integer, integer, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_studio_aliases(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_tag_aliases(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_performer_aliases(integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_entity_identifiers(text, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_custom_fields(text, integer) CASCADE;
DROP FUNCTION IF EXISTS cove_search_clean(text) CASCADE;
""";
    }
}
