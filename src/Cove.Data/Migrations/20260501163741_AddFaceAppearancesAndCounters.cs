using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceAppearancesAndCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppearanceCount",
                table: "faces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FrameSampleCount",
                table: "faces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "face_appearances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FaceId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAtSec = table.Column<double>(type: "double precision", nullable: true),
                    LastSeenAtSec = table.Column<double>(type: "double precision", nullable: true),
                    SampleCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RetainedSpatialSampleCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SegmentCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RepresentativeFrameSec = table.Column<double>(type: "double precision", nullable: true),
                    TopConfidence = table.Column<float>(type: "real", nullable: true),
                    GroupKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceRunId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_appearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_face_appearances_faces_FaceId",
                        column: x => x.FaceId,
                        principalTable: "faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_FaceId",
                table: "face_appearances",
                column: "FaceId");

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_FaceId_HostType_HostId",
                table: "face_appearances",
                columns: new[] { "FaceId", "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_GroupKey",
                table: "face_appearances",
                column: "GroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_HostType_HostId",
                table: "face_appearances",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_SourceKey",
                table: "face_appearances",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_face_appearances_SourceRunId",
                table: "face_appearances",
                column: "SourceRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "face_appearances");

            migrationBuilder.DropColumn(
                name: "AppearanceCount",
                table: "faces");

            migrationBuilder.DropColumn(
                name: "FrameSampleCount",
                table: "faces");
        }
    }
}
