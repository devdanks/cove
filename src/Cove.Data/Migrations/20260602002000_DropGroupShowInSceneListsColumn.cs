using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CoveContext))]
    [Migration("20260602002000_DropGroupShowInSceneListsColumn")]
    public partial class DropGroupShowInSceneListsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.groups DROP COLUMN IF EXISTS \"ShowInSceneLists\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE IF EXISTS public.groups ADD COLUMN \"ShowInSceneLists\" boolean NOT NULL DEFAULT FALSE;");
        }
    }
}
