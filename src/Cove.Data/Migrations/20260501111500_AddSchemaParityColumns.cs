using Cove.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    [DbContext(typeof(CoveContext))]
    [Migration("20260501111500_AddSchemaParityColumns")]
    public partial class AddSchemaParityColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"scenes\" ADD COLUMN IF NOT EXISTS \"ImageBlobId\" text;");
            migrationBuilder.Sql("ALTER TABLE \"scrape_attempts\" ADD COLUMN IF NOT EXISTS \"CandidateResultsJson\" text;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_files_ImageId\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_files_SceneId\";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"scrape_attempts\" DROP COLUMN IF EXISTS \"CandidateResultsJson\";");
            migrationBuilder.Sql("ALTER TABLE \"scenes\" DROP COLUMN IF EXISTS \"ImageBlobId\";");
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