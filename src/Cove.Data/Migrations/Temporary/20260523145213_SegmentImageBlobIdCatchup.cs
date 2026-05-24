using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations.Temporary
{
    /// <inheritdoc />
    public partial class SegmentImageBlobIdCatchup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.segments ADD COLUMN IF NOT EXISTS \"ImageBlobId\" text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.segments DROP COLUMN IF EXISTS \"ImageBlobId\";");
        }
    }
}
