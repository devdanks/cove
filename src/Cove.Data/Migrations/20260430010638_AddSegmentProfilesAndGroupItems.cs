using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentProfilesAndGroupItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_segment_display_rules_SourceKey_Kind_TagId_TagCategory_Host~",
                table: "segment_display_rules");

            migrationBuilder.CreateTable(
                name: "segment_display_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segment_display_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "group_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    StartSec = table.Column<double>(type: "double precision", nullable: true),
                    EndSec = table.Column<double>(type: "double precision", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SourceSpanKey = table.Column<string>(type: "text", nullable: true),
                    SourceProfileId = table.Column<int>(type: "integer", nullable: true),
                    SnapshotAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_group_items_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_items_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "segment_display_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                VALUES ('Raw', 'Built-in raw segment display profile', NULL, TRUE, FALSE, 1, now(), now());

                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                VALUES ('Default', 'Built-in default segment display profile', NULL, TRUE, TRUE, 1, now(), now());

                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT 'Default', 'User default segment display profile', rules."UserId", FALSE, TRUE, 1, now(), now()
                FROM "segment_display_rules" rules
                WHERE rules."UserId" IS NOT NULL;

                UPDATE "segment_display_rules" AS rules
                SET "ProfileId" = profiles."Id"
                FROM "segment_display_profiles" AS profiles
                WHERE (rules."ProfileId" IS NULL)
                  AND (
                      (rules."UserId" IS NULL AND profiles."UserId" IS NULL AND profiles."IsDefault" = TRUE)
                      OR (rules."UserId" IS NOT NULL AND profiles."UserId" = rules."UserId" AND profiles."IsDefault" = TRUE)
                  );

                INSERT INTO "segment_display_rules" ("ProfileId", "SourceKey", "Visible", "MinDurationSec", "MergeGapSec", "CollapseToInstant", "CreatedAt", "UpdatedAt")
                SELECT profiles."Id", 'ext:ai.%', TRUE, 1.5, 2.0, FALSE, now(), now()
                FROM "segment_display_profiles" profiles
                WHERE profiles."UserId" IS NULL AND profiles."IsDefault" = TRUE;

                INSERT INTO "group_items" ("GroupId", "OrderIndex", "Kind", "SceneId", "CreatedAt", "UpdatedAt")
                SELECT sg."GroupId", sg."SceneIndex", 1, sg."SceneId", now(), now()
                FROM "scene_groups" sg;
            """);

            migrationBuilder.AlterColumn<int>(
                name: "ProfileId",
                table: "segment_display_rules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_ProfileId",
                table: "segment_display_rules",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_ProfileId_SourceKey_Kind_TagId_TagCat~",
                table: "segment_display_rules",
                columns: new[] { "ProfileId", "SourceKey", "Kind", "TagId", "TagCategory", "HostType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_GroupId_OrderIndex",
                table: "group_items",
                columns: new[] { "GroupId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_SceneId",
                table: "group_items",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_SourceProfileId",
                table: "group_items",
                column: "SourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_profiles_IsSystem_Name",
                table: "segment_display_profiles",
                columns: new[] { "IsSystem", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_profiles_UserId",
                table: "segment_display_profiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_profiles_UserId_IsDefault",
                table: "segment_display_profiles",
                columns: new[] { "UserId", "IsDefault" });

            migrationBuilder.AddForeignKey(
                name: "FK_segment_display_rules_segment_display_profiles_ProfileId",
                table: "segment_display_rules",
                column: "ProfileId",
                principalTable: "segment_display_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_segment_display_rules_segment_display_profiles_ProfileId",
                table: "segment_display_rules");

            migrationBuilder.DropTable(
                name: "group_items");

            migrationBuilder.DropTable(
                name: "segment_display_profiles");

            migrationBuilder.DropIndex(
                name: "IX_segment_display_rules_ProfileId",
                table: "segment_display_rules");

            migrationBuilder.DropIndex(
                name: "IX_segment_display_rules_ProfileId_SourceKey_Kind_TagId_TagCat~",
                table: "segment_display_rules");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "segment_display_rules");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_SourceKey_Kind_TagId_TagCategory_Host~",
                table: "segment_display_rules",
                columns: new[] { "SourceKey", "Kind", "TagId", "TagCategory", "HostType", "Priority" });
        }
    }
}
