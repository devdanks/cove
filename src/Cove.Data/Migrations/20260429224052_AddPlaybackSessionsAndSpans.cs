using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackSessionsAndSpans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastPositionSec = table.Column<double>(type: "double precision", nullable: true),
                    LastReportedDurationSec = table.Column<double>(type: "double precision", nullable: false),
                    TotalConsumedSec = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackSpans",
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
                    ObservedStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_PlaybackSessions_UserId_HostType_HostId_StartedAt",
                table: "PlaybackSessions",
                columns: new[] { "UserId", "HostType", "HostId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSessions_UserId_SessionId",
                table: "PlaybackSessions",
                columns: new[] { "UserId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSpans_PlaybackSessionId_StartSec",
                table: "PlaybackSpans",
                columns: new[] { "PlaybackSessionId", "StartSec" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSpans_UserId_HostType_HostId_StartSec_EndSec",
                table: "PlaybackSpans",
                columns: new[] { "UserId", "HostType", "HostId", "StartSec", "EndSec" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackSpans");

            migrationBuilder.DropTable(
                name: "PlaybackSessions");
        }
    }
}
