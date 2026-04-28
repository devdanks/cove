using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagPerformerArrays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "PerformerIds",
                table: "scenes",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "TagIds",
                table: "scenes",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "PerformerIds",
                table: "images",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "TagIds",
                table: "images",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "PerformerIds",
                table: "galleries",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int[]>(
                name: "TagIds",
                table: "galleries",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            // Backfill arrays from join tables in one statement per (parent, link) pair
            // so GIN indexes are created over already-populated columns. Using array_agg
            // with ORDER BY produces stable, sorted arrays for predictable diffs.
            migrationBuilder.Sql(@"
                UPDATE scenes SET ""TagIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""SceneId"" AS sid, array_agg(""TagId"" ORDER BY ""TagId"") AS ids
                      FROM scene_tags GROUP BY ""SceneId"") t
                WHERE scenes.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE scenes SET ""PerformerIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""SceneId"" AS sid, array_agg(""PerformerId"" ORDER BY ""PerformerId"") AS ids
                      FROM scene_performers GROUP BY ""SceneId"") t
                WHERE scenes.""Id"" = t.sid;
            ");
            migrationBuilder.Sql(@"
                UPDATE images SET ""TagIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""ImageId"" AS iid, array_agg(""TagId"" ORDER BY ""TagId"") AS ids
                      FROM image_tags GROUP BY ""ImageId"") t
                WHERE images.""Id"" = t.iid;
            ");
            migrationBuilder.Sql(@"
                UPDATE images SET ""PerformerIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""ImageId"" AS iid, array_agg(""PerformerId"" ORDER BY ""PerformerId"") AS ids
                      FROM image_performers GROUP BY ""ImageId"") t
                WHERE images.""Id"" = t.iid;
            ");
            migrationBuilder.Sql(@"
                UPDATE galleries SET ""TagIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""GalleryId"" AS gid, array_agg(""TagId"" ORDER BY ""TagId"") AS ids
                      FROM gallery_tags GROUP BY ""GalleryId"") t
                WHERE galleries.""Id"" = t.gid;
            ");
            migrationBuilder.Sql(@"
                UPDATE galleries SET ""PerformerIds"" = COALESCE(t.ids, ARRAY[]::int[])
                FROM (SELECT ""GalleryId"" AS gid, array_agg(""PerformerId"" ORDER BY ""PerformerId"") AS ids
                      FROM gallery_performers GROUP BY ""GalleryId"") t
                WHERE galleries.""Id"" = t.gid;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_PerformerIds",
                table: "scenes",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_TagIds",
                table: "scenes",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_PerformerIds",
                table: "images",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_TagIds",
                table: "images",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_PerformerIds",
                table: "galleries",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_TagIds",
                table: "galleries",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scenes_PerformerIds",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_TagIds",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_images_PerformerIds",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_TagIds",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_galleries_PerformerIds",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_TagIds",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "PerformerIds",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "TagIds",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "PerformerIds",
                table: "images");

            migrationBuilder.DropColumn(
                name: "TagIds",
                table: "images");

            migrationBuilder.DropColumn(
                name: "PerformerIds",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "TagIds",
                table: "galleries");
        }
    }
}
