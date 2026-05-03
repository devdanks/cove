using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class RebuildPlaybackTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackSpans");

            migrationBuilder.DropIndex(
                name: "IX_interactions_UserId_SessionId_Kind_At",
                table: "interactions");

            migrationBuilder.DropColumn(
                name: "DurationSec",
                table: "interactions");

            migrationBuilder.DropColumn(
                name: "PositionSec",
                table: "interactions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "interactions");

            migrationBuilder.RenameColumn(
                name: "TotalConsumedSec",
                table: "PlaybackSessions",
                newName: "TotalWatchedSec");

            migrationBuilder.RenameColumn(
                name: "LastReportedDurationSec",
                table: "PlaybackSessions",
                newName: "MediaDurationSec");

            migrationBuilder.AddColumn<bool>(
                name: "CountsAsView",
                table: "PlaybackSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlaybackIntervals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaybackSessionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    StartSec = table.Column<double>(type: "double precision", nullable: false),
                    EndSec = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackIntervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackIntervals_PlaybackSessions_PlaybackSessionId",
                        column: x => x.PlaybackSessionId,
                        principalTable: "PlaybackSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackIntervals_PlaybackSessionId_StartSec",
                table: "PlaybackIntervals",
                columns: new[] { "PlaybackSessionId", "StartSec" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackIntervals_UserId_HostType_HostId",
                table: "PlaybackIntervals",
                columns: new[] { "UserId", "HostType", "HostId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackIntervals");

            migrationBuilder.DropColumn(
                name: "CountsAsView",
                table: "PlaybackSessions");

            migrationBuilder.RenameColumn(
                name: "TotalWatchedSec",
                table: "PlaybackSessions",
                newName: "TotalConsumedSec");

            migrationBuilder.RenameColumn(
                name: "MediaDurationSec",
                table: "PlaybackSessions",
                newName: "LastReportedDurationSec");

            migrationBuilder.AddColumn<double>(
                name: "DurationSec",
                table: "interactions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PositionSec",
                table: "interactions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "interactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaybackSpans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaybackSessionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndSec = table.Column<double>(type: "double precision", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    ObservedEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartSec = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSpans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackSpans_PlaybackSessions_PlaybackSessionId",
                        column: x => x.PlaybackSessionId,
                        principalTable: "PlaybackSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_SessionId_Kind_At",
                table: "interactions",
                columns: new[] { "UserId", "SessionId", "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSpans_PlaybackSessionId_StartSec",
                table: "PlaybackSpans",
                columns: new[] { "PlaybackSessionId", "StartSec" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSpans_UserId_HostType_HostId_StartSec_EndSec",
                table: "PlaybackSpans",
                columns: new[] { "UserId", "HostType", "HostId", "StartSec", "EndSec" });
        }
    }
}
