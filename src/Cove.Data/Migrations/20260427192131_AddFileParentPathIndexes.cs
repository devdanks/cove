using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileParentPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_files_GalleryId",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_ImageId",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_SceneId",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "IX_files_GalleryId_Path",
                table: "files",
                columns: new[] { "GalleryId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_ImageId_Path",
                table: "files",
                columns: new[] { "ImageId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_SceneId_Path",
                table: "files",
                columns: new[] { "SceneId", "Path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_files_GalleryId_Path",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_ImageId_Path",
                table: "files");

            migrationBuilder.DropIndex(
                name: "IX_files_SceneId_Path",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "IX_files_GalleryId",
                table: "files",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_files_ImageId",
                table: "files",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_files_SceneId",
                table: "files",
                column: "SceneId");
        }
    }
}
