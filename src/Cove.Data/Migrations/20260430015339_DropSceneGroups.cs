using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropSceneGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    relation_kind text;
                BEGIN
                    SELECT c.relkind::text
                    INTO relation_kind
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema()
                      AND c.relname = 'scene_groups';

                    IF relation_kind IN ('r', 'p') THEN
                        EXECUTE 'DROP TABLE IF EXISTS "scene_groups"';
                    ELSIF relation_kind = 'v' THEN
                        EXECUTE 'DROP VIEW IF EXISTS "scene_groups"';
                    ELSIF relation_kind = 'm' THEN
                        EXECUTE 'DROP MATERIALIZED VIEW IF EXISTS "scene_groups"';
                    ELSIF relation_kind = 'f' THEN
                        EXECUTE 'DROP FOREIGN TABLE IF EXISTS "scene_groups"';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scene_groups",
                columns: table => new
                {
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    SceneIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_groups", x => new { x.SceneId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_scene_groups_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_groups_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scene_groups_GroupId",
                table: "scene_groups",
                column: "GroupId");
        }
    }
}
