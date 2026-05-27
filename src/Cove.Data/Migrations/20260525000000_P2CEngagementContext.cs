using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CoveContext))]
    [Migration("20260525000000_P2CEngagementContext")]
    public partial class P2CEngagementContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Surface", table: "PlaybackSessions", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ScopeKey", table: "PlaybackSessions", type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<int>(name: "ParentHostType", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ParentHostId", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ItemHostType", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ItemHostId", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "GroupItemId", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "SegmentId", table: "PlaybackSessions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<double>(name: "ClipStartSec", table: "PlaybackSessions", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "ClipEndSec", table: "PlaybackSessions", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Autoplay", table: "PlaybackSessions", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Muted", table: "PlaybackSessions", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "Fullscreen", table: "PlaybackSessions", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<double>(name: "PlaybackRate", table: "PlaybackSessions", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Route", table: "PlaybackSessions", type: "character varying(512)", maxLength: 512, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Referrer", table: "PlaybackSessions", type: "character varying(512)", maxLength: 512, nullable: true);
            migrationBuilder.AddColumn<string>(name: "RecommendationSource", table: "PlaybackSessions", type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<JsonDocument>(name: "Context", table: "PlaybackSessions", type: "jsonb", nullable: true);

            migrationBuilder.AddColumn<string>(name: "Surface", table: "PlaybackIntervals", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ScopeKey", table: "PlaybackIntervals", type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<int>(name: "ParentHostType", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ParentHostId", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ItemHostType", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ItemHostId", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "GroupItemId", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "SegmentId", table: "PlaybackIntervals", type: "integer", nullable: true);
            migrationBuilder.AddColumn<double>(name: "ClipStartSec", table: "PlaybackIntervals", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "ClipEndSec", table: "PlaybackIntervals", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "PlaybackRate", table: "PlaybackIntervals", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<JsonDocument>(name: "Context", table: "PlaybackIntervals", type: "jsonb", nullable: true);

            migrationBuilder.AddColumn<int>(name: "InteractionCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "LastInteractedAt", table: "user_entity_affinities", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OpenDetailCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "OpenLightboxCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "NavigateCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "PauseCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "SeekCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "PlayerControlCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "SearchInteractionCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "FilterInteractionCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ShareCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "HideCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "ZoomCount", table: "user_entity_affinities", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackSessions_UserId_Surface_LastSeenAt",
                table: "PlaybackSessions",
                columns: new[] { "UserId", "Surface", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackIntervals_UserId_Surface_RecordedAt",
                table: "PlaybackIntervals",
                columns: new[] { "UserId", "Surface", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_PlaybackSessions_UserId_Surface_LastSeenAt", table: "PlaybackSessions");
            migrationBuilder.DropIndex(name: "IX_PlaybackIntervals_UserId_Surface_RecordedAt", table: "PlaybackIntervals");

            migrationBuilder.DropColumn(name: "Surface", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ScopeKey", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ParentHostType", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ParentHostId", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ItemHostType", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ItemHostId", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "GroupItemId", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "SegmentId", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ClipStartSec", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "ClipEndSec", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Autoplay", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Muted", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Fullscreen", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "PlaybackRate", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Route", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Referrer", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "RecommendationSource", table: "PlaybackSessions");
            migrationBuilder.DropColumn(name: "Context", table: "PlaybackSessions");

            migrationBuilder.DropColumn(name: "Surface", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ScopeKey", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ParentHostType", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ParentHostId", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ItemHostType", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ItemHostId", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "GroupItemId", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "SegmentId", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ClipStartSec", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "ClipEndSec", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "PlaybackRate", table: "PlaybackIntervals");
            migrationBuilder.DropColumn(name: "Context", table: "PlaybackIntervals");

            migrationBuilder.DropColumn(name: "InteractionCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "LastInteractedAt", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "OpenDetailCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "OpenLightboxCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "NavigateCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "PauseCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "SeekCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "PlayerControlCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "SearchInteractionCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "FilterInteractionCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "ShareCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "HideCount", table: "user_entity_affinities");
            migrationBuilder.DropColumn(name: "ZoomCount", table: "user_entity_affinities");
        }
    }
}