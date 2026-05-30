using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations.Temporary
{
    /// <inheritdoc />
    public partial class GalleryBackImageBlobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.galleries ADD COLUMN IF NOT EXISTS \"BackImageBlobId\" text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.galleries DROP COLUMN IF EXISTS \"BackImageBlobId\";");
        }
    }
}
