using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CoveContext))]
    [Migration("20260602000000_GroupShowInVideoListsRepair")]
    public partial class GroupShowInVideoListsRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.groups ADD COLUMN IF NOT EXISTS \"ShowInVideoLists\" boolean NOT NULL DEFAULT FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.groups DROP COLUMN IF EXISTS \"ShowInVideoLists\";");
        }
    }
}
