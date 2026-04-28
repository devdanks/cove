using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneImageSearchAndOrientationFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileSearchText",
                table: "scenes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasDimensionData",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasInteractiveFiles",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLandscapeFiles",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNonInteractiveFiles",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPortraitFiles",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSquareFiles",
                table: "scenes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FileSearchText",
                table: "images",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasDimensionData",
                table: "images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLandscapeFiles",
                table: "images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasPortraitFiles",
                table: "images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSquareFiles",
                table: "images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
UPDATE scenes AS s
SET
    "FileSearchText" = summary."FileSearchText",
    "HasDimensionData" = summary."HasDimensionData",
    "HasInteractiveFiles" = summary."HasInteractiveFiles",
    "HasLandscapeFiles" = summary."HasLandscapeFiles",
    "HasNonInteractiveFiles" = summary."HasNonInteractiveFiles",
    "HasPortraitFiles" = summary."HasPortraitFiles",
    "HasSquareFiles" = summary."HasSquareFiles"
FROM (
    SELECT
        "SceneId",
        E'\n' || string_agg("Path", E'\n' ORDER BY "Path") || E'\n' AS "FileSearchText",
        COALESCE(bool_or("Width" > 0 AND "Height" > 0), FALSE) AS "HasDimensionData",
        COALESCE(bool_or("Interactive"), FALSE) AS "HasInteractiveFiles",
        COALESCE(bool_or("Width" > "Height"), FALSE) AS "HasLandscapeFiles",
        COALESCE(bool_or(NOT "Interactive"), FALSE) AS "HasNonInteractiveFiles",
        COALESCE(bool_or("Height" > "Width"), FALSE) AS "HasPortraitFiles",
        COALESCE(bool_or("Width" > 0 AND "Width" = "Height"), FALSE) AS "HasSquareFiles"
    FROM files
    WHERE "FileType" = 'Video' AND "SceneId" IS NOT NULL
    GROUP BY "SceneId"
) AS summary
WHERE s."Id" = summary."SceneId";

UPDATE images AS i
SET
    "FileSearchText" = summary."FileSearchText",
    "HasDimensionData" = summary."HasDimensionData",
    "HasLandscapeFiles" = summary."HasLandscapeFiles",
    "HasPortraitFiles" = summary."HasPortraitFiles",
    "HasSquareFiles" = summary."HasSquareFiles"
FROM (
    SELECT
        "ImageId",
        E'\n' || string_agg("Path", E'\n' ORDER BY "Path") || E'\n' AS "FileSearchText",
        COALESCE(bool_or("Width" > 0 AND "Height" > 0), FALSE) AS "HasDimensionData",
        COALESCE(bool_or("Width" > "Height"), FALSE) AS "HasLandscapeFiles",
        COALESCE(bool_or("Height" > "Width"), FALSE) AS "HasPortraitFiles",
        COALESCE(bool_or("Width" > 0 AND "Width" = "Height"), FALSE) AS "HasSquareFiles"
    FROM files
    WHERE "FileType" = 'Image' AND "ImageId" IS NOT NULL
    GROUP BY "ImageId"
) AS summary
WHERE i."Id" = summary."ImageId";
""");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasDimensionData",
                table: "scenes",
                column: "HasDimensionData");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasInteractiveFiles",
                table: "scenes",
                column: "HasInteractiveFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasLandscapeFiles",
                table: "scenes",
                column: "HasLandscapeFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasNonInteractiveFiles",
                table: "scenes",
                column: "HasNonInteractiveFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasPortraitFiles",
                table: "scenes",
                column: "HasPortraitFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasSquareFiles",
                table: "scenes",
                column: "HasSquareFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasDimensionData",
                table: "images",
                column: "HasDimensionData");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasLandscapeFiles",
                table: "images",
                column: "HasLandscapeFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasPortraitFiles",
                table: "images",
                column: "HasPortraitFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasSquareFiles",
                table: "images",
                column: "HasSquareFiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scenes_HasDimensionData",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_HasInteractiveFiles",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_HasLandscapeFiles",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_HasNonInteractiveFiles",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_HasPortraitFiles",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_HasSquareFiles",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_images_HasDimensionData",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_HasLandscapeFiles",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_HasPortraitFiles",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_HasSquareFiles",
                table: "images");

            migrationBuilder.DropColumn(
                name: "FileSearchText",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasDimensionData",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasInteractiveFiles",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasLandscapeFiles",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasNonInteractiveFiles",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasPortraitFiles",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "HasSquareFiles",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "FileSearchText",
                table: "images");

            migrationBuilder.DropColumn(
                name: "HasDimensionData",
                table: "images");

            migrationBuilder.DropColumn(
                name: "HasLandscapeFiles",
                table: "images");

            migrationBuilder.DropColumn(
                name: "HasPortraitFiles",
                table: "images");

            migrationBuilder.DropColumn(
                name: "HasSquareFiles",
                table: "images");
        }
    }
}
