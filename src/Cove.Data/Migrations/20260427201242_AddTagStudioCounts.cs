using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagStudioCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GalleryCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PerformerCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SceneCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SceneMarkerCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StudioCount",
                table: "tags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChildStudioCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GalleryCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PerformerCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SceneCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TagCount",
                table: "studios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE tags SET ""SceneCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM scene_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""SceneMarkerCount"" = t.count
                FROM (SELECT ""PrimaryTagId"" AS tid, COUNT(*) AS count
                      FROM scene_markers GROUP BY ""PrimaryTagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""SceneMarkerCount"" = tags.""SceneMarkerCount"" + t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM scene_marker_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""ImageCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM image_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""GalleryCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM gallery_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""GroupCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM group_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""PerformerCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM performer_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");
            migrationBuilder.Sql(@"
                UPDATE tags SET ""StudioCount"" = t.count
                FROM (SELECT ""TagId"" AS tid, COUNT(*) AS count
                      FROM studio_tags GROUP BY ""TagId"") t
                WHERE tags.""Id"" = t.tid;
            ");

            migrationBuilder.Sql(@"
                UPDATE studios SET ""SceneCount"" = t.count
                FROM (SELECT ""StudioId"" AS sid, COUNT(*) AS count
                      FROM scenes WHERE ""StudioId"" IS NOT NULL GROUP BY ""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""ImageCount"" = t.count
                FROM (SELECT ""StudioId"" AS sid, COUNT(*) AS count
                      FROM images WHERE ""StudioId"" IS NOT NULL GROUP BY ""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""GalleryCount"" = t.count
                FROM (SELECT ""StudioId"" AS sid, COUNT(*) AS count
                      FROM galleries WHERE ""StudioId"" IS NOT NULL GROUP BY ""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""GroupCount"" = t.count
                FROM (SELECT ""StudioId"" AS sid, COUNT(*) AS count
                      FROM groups WHERE ""StudioId"" IS NOT NULL GROUP BY ""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""PerformerCount"" = t.count
                FROM (SELECT scenes.""StudioId"" AS sid, COUNT(DISTINCT scene_performers.""PerformerId"") AS count
                      FROM scenes
                      JOIN scene_performers ON scene_performers.""SceneId"" = scenes.""Id""
                      WHERE scenes.""StudioId"" IS NOT NULL
                      GROUP BY scenes.""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""ChildStudioCount"" = t.count
                FROM (SELECT ""ParentId"" AS sid, COUNT(*) AS count
                      FROM studios WHERE ""ParentId"" IS NOT NULL GROUP BY ""ParentId"") t
                WHERE studios.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE studios SET ""TagCount"" = t.count
                FROM (SELECT ""StudioId"" AS sid, COUNT(*) AS count
                      FROM studio_tags GROUP BY ""StudioId"") t
                WHERE studios.""Id"" = t.sid;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GalleryCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "GroupCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "ImageCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "PerformerCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SceneCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "SceneMarkerCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "StudioCount",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "ChildStudioCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "GalleryCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "GroupCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "ImageCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "PerformerCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "SceneCount",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "TagCount",
                table: "studios");
        }
    }
}
