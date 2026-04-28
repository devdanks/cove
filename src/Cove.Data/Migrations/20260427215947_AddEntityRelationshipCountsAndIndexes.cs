using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityRelationshipCountsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GalleryCount",
                table: "performers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "performers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SceneCount",
                table: "performers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TagCount",
                table: "performers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GalleryCount",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PerformerCount",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TagCount",
                table: "images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageCount",
                table: "galleries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PerformerCount",
                table: "galleries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SceneCount",
                table: "galleries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TagCount",
                table: "galleries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE performers AS p
                SET "SceneCount" = (SELECT COUNT(*)::integer FROM scene_performers AS sp WHERE sp."PerformerId" = p."Id"),
                    "ImageCount" = (SELECT COUNT(*)::integer FROM image_performers AS ip WHERE ip."PerformerId" = p."Id"),
                    "GalleryCount" = (SELECT COUNT(*)::integer FROM gallery_performers AS gp WHERE gp."PerformerId" = p."Id"),
                    "TagCount" = (SELECT COUNT(*)::integer FROM performer_tags AS pt WHERE pt."PerformerId" = p."Id");

                UPDATE images AS i
                SET "TagCount" = (SELECT COUNT(*)::integer FROM image_tags AS it WHERE it."ImageId" = i."Id"),
                    "PerformerCount" = (SELECT COUNT(*)::integer FROM image_performers AS ip WHERE ip."ImageId" = i."Id"),
                    "GalleryCount" = (SELECT COUNT(*)::integer FROM image_galleries AS ig WHERE ig."ImageId" = i."Id");

                UPDATE galleries AS g
                SET "ImageCount" = (SELECT COUNT(*)::integer FROM image_galleries AS ig WHERE ig."GalleryId" = g."Id"),
                    "SceneCount" = (SELECT COUNT(*)::integer FROM scene_galleries AS sg WHERE sg."GalleryId" = g."Id"),
                    "PerformerCount" = (SELECT COUNT(*)::integer FROM gallery_performers AS gp WHERE gp."GalleryId" = g."Id"),
                    "TagCount" = (SELECT COUNT(*)::integer FROM gallery_tags AS gt WHERE gt."GalleryId" = g."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_tags_GalleryCount",
                table: "tags",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_GroupCount",
                table: "tags",
                column: "GroupCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_ImageCount",
                table: "tags",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_PerformerCount",
                table: "tags",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SceneCount",
                table: "tags",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SceneMarkerCount",
                table: "tags",
                column: "SceneMarkerCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_StudioCount",
                table: "tags",
                column: "StudioCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_ChildStudioCount",
                table: "studios",
                column: "ChildStudioCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Favorite",
                table: "studios",
                column: "Favorite");

            migrationBuilder.CreateIndex(
                name: "IX_studios_GalleryCount",
                table: "studios",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_GroupCount",
                table: "studios",
                column: "GroupCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_ImageCount",
                table: "studios",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Organized",
                table: "studios",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_studios_PerformerCount",
                table: "studios",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Rating",
                table: "studios",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_studios_SceneCount",
                table: "studios",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_TagCount",
                table: "studios",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_GalleryCount",
                table: "performers",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_ImageCount",
                table: "performers",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_SceneCount",
                table: "performers",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_TagCount",
                table: "performers",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_GalleryCount",
                table: "images",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_PerformerCount",
                table: "images",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_TagCount",
                table: "images",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_CreatedAt",
                table: "galleries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Date",
                table: "galleries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_ImageCount",
                table: "galleries",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Organized",
                table: "galleries",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_PerformerCount",
                table: "galleries",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Rating",
                table: "galleries",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_SceneCount",
                table: "galleries",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_TagCount",
                table: "galleries",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_UpdatedAt",
                table: "galleries",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tags_GalleryCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_GroupCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_ImageCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_PerformerCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_SceneCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_SceneMarkerCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_StudioCount",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_studios_ChildStudioCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_Favorite",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_GalleryCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_GroupCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_ImageCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_Organized",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_PerformerCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_Rating",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_SceneCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_studios_TagCount",
                table: "studios");

            migrationBuilder.DropIndex(
                name: "IX_performers_GalleryCount",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_performers_ImageCount",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_performers_SceneCount",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_performers_TagCount",
                table: "performers");

            migrationBuilder.DropIndex(
                name: "IX_images_GalleryCount",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_PerformerCount",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_images_TagCount",
                table: "images");

            migrationBuilder.DropIndex(
                name: "IX_galleries_CreatedAt",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_Date",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_ImageCount",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_Organized",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_PerformerCount",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_Rating",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_SceneCount",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_TagCount",
                table: "galleries");

            migrationBuilder.DropIndex(
                name: "IX_galleries_UpdatedAt",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "GalleryCount",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "ImageCount",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "SceneCount",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "TagCount",
                table: "performers");

            migrationBuilder.DropColumn(
                name: "GalleryCount",
                table: "images");

            migrationBuilder.DropColumn(
                name: "PerformerCount",
                table: "images");

            migrationBuilder.DropColumn(
                name: "TagCount",
                table: "images");

            migrationBuilder.DropColumn(
                name: "ImageCount",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "PerformerCount",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "SceneCount",
                table: "galleries");

            migrationBuilder.DropColumn(
                name: "TagCount",
                table: "galleries");
        }
    }
}
