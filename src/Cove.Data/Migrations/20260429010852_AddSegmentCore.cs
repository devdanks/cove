using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "detections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ObservedAtSec = table.Column<double>(type: "double precision", nullable: true),
                    FrameWidth = table.Column<int>(type: "integer", nullable: false),
                    FrameHeight = table.Column<int>(type: "integer", nullable: false),
                    Class = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<float>(type: "real", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    W = table.Column<float>(type: "real", nullable: false),
                    H = table.Column<float>(type: "real", nullable: false),
                    Extra = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    RefKind = table.Column<string>(type: "text", nullable: true),
                    RefId = table.Column<long>(type: "bigint", nullable: true),
                    GroupKey = table.Column<string>(type: "text", nullable: true),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "segment_display_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceKey = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: true),
                    TagId = table.Column<int>(type: "integer", nullable: true),
                    TagCategory = table.Column<string>(type: "text", nullable: true),
                    HostType = table.Column<int>(type: "integer", nullable: true),
                    Visible = table.Column<bool>(type: "boolean", nullable: false),
                    MinConfidence = table.Column<float>(type: "real", nullable: true),
                    MinDurationSec = table.Column<double>(type: "double precision", nullable: true),
                    MergeGapSec = table.Column<double>(type: "double precision", nullable: true),
                    CollapseToInstant = table.Column<bool>(type: "boolean", nullable: false),
                    ColorOverride = table.Column<string>(type: "text", nullable: true),
                    Lane = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segment_display_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_segment_display_rules_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    StartSec = table.Column<double>(type: "double precision", nullable: false),
                    EndSec = table.Column<double>(type: "double precision", nullable: true),
                    TagId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: true),
                    RefId = table.Column<long>(type: "bigint", nullable: true),
                    Payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    ColorHint = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_segments_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detections_Class",
                table: "detections",
                column: "Class");

            migrationBuilder.CreateIndex(
                name: "IX_detections_GroupKey",
                table: "detections",
                column: "GroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_detections_HostType_HostId_ObservedAtSec",
                table: "detections",
                columns: new[] { "HostType", "HostId", "ObservedAtSec" });

            migrationBuilder.CreateIndex(
                name: "IX_detections_RefKind_RefId",
                table: "detections",
                columns: new[] { "RefKind", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_detections_SourceKey",
                table: "detections",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_detections_SourceRunId",
                table: "detections",
                column: "SourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_SourceKey_Kind_TagId_TagCategory_Host~",
                table: "segment_display_rules",
                columns: new[] { "SourceKey", "Kind", "TagId", "TagCategory", "HostType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_TagId",
                table: "segment_display_rules",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_UserId",
                table: "segment_display_rules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_segments_HostType_HostId_StartSec",
                table: "segments",
                columns: new[] { "HostType", "HostId", "StartSec" });

            migrationBuilder.CreateIndex(
                name: "IX_segments_Kind",
                table: "segments",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_segments_SourceKey",
                table: "segments",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_segments_SourceRunId",
                table: "segments",
                column: "SourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_segments_TagId",
                table: "segments",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detections");

            migrationBuilder.DropTable(
                name: "segment_display_rules");

            migrationBuilder.DropTable(
                name: "segments");
        }
    }
}
