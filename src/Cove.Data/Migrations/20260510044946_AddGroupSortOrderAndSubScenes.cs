using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSortOrderAndSubScenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ClipEndSec",
                table: "scenes",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClipStartSec",
                table: "scenes",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentSceneId",
                table: "scenes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_scenes_ParentSceneId",
                table: "scenes",
                column: "ParentSceneId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_SortOrder",
                table: "groups",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_scenes_scenes_ParentSceneId",
                table: "scenes",
                column: "ParentSceneId",
                principalTable: "scenes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_scenes_scenes_ParentSceneId",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_scenes_ParentSceneId",
                table: "scenes");

            migrationBuilder.DropIndex(
                name: "IX_groups_SortOrder",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "ClipEndSec",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "ClipStartSec",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "ParentSceneId",
                table: "scenes");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "groups");
        }
    }
}
