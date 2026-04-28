using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class DenormalizeFilePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add denormalized full-path column on files. Backfill from folders + basename
            // (forward-slash form) so list endpoints can sort/filter on path index-only
            // instead of via a per-row correlated subquery.
            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "files",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Backfill existing rows. Folder paths may use backslashes on Windows-imported
            // data; normalize to forward slashes here.
            migrationBuilder.Sql(@"
                UPDATE files AS f
                SET ""Path"" = REPLACE(folders.""Path"", '\', '/') ||
                    CASE
                        WHEN folders.""Path"" = '' THEN ''
                        WHEN RIGHT(folders.""Path"", 1) IN ('/', '\') THEN ''
                        ELSE '/'
                    END || f.""Basename""
                FROM folders
                WHERE f.""ParentFolderId"" = folders.""Id"";
            ");

            // NOTE: folders.Path is intentionally NOT bulk-normalized here. Some
            // existing databases contain a mix of forward-slash and back-slash folder
            // paths whose normalized forms would collide against IX_folders_Path
            // (unique). New rows go through CoveContext.ComputeFilePaths which
            // normalizes on save; legacy rows can be normalized via a one-off
            // maintenance script.

            migrationBuilder.CreateIndex(
                name: "IX_files_Path",
                table: "files",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_files_Path",
                table: "files");

            migrationBuilder.DropColumn(
                name: "Path",
                table: "files");
        }
    }
}
