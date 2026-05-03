using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLayer2CoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoadPolicy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FrameIntervalSec = table.Column<double>(type: "double precision", nullable: true),
                    Vr = table.Column<bool>(type: "boolean", nullable: true),
                    Request = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    Models = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    Summary = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "embeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KindFamily = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Modality = table.Column<int>(type: "integer", nullable: false),
                    IsSemantic = table.Column<bool>(type: "boolean", nullable: false),
                    Dim = table.Column<int>(type: "integer", nullable: false),
                    Vector = table.Column<string>(type: "text", nullable: false),
                    SectionIndex = table.Column<int>(type: "integer", nullable: false),
                    StartSec = table.Column<double>(type: "double precision", nullable: true),
                    EndSec = table.Column<double>(type: "double precision", nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceRunId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Meta = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embeddings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "faces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PerformerId = table.Column<int>(type: "integer", nullable: true),
                    CoverBlobId = table.Column<string>(type: "text", nullable: true),
                    Ignored = table.Column<bool>(type: "boolean", nullable: false),
                    MergedIntoFaceId = table.Column<int>(type: "integer", nullable: true),
                    DetectionCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PrimarySourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomFields = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faces_faces_MergedIntoFaceId",
                        column: x => x.MergedIntoFaceId,
                        principalTable: "faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_faces_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "interactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PositionSec = table.Column<double>(type: "double precision", nullable: true),
                    DurationSec = table.Column<double>(type: "double precision", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Meta = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ratings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Aspect = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ratings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_entity_affinities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    FavoritedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CompleteCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalConsumedSec = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0d),
                    LastPositionSec = table.Column<double>(type: "double precision", nullable: true),
                    LastConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_entity_affinities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_JobId",
                table: "ai_runs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_RunKey",
                table: "ai_runs",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_SourceKey",
                table: "ai_runs",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_Status",
                table: "ai_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ai_runs_TargetType_TargetId_CreatedAt",
                table: "ai_runs",
                columns: new[] { "TargetType", "TargetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_HostType_HostId",
                table: "embeddings",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_Kind_Dim",
                table: "embeddings",
                columns: new[] { "Kind", "Dim" });

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_KindFamily_Modality",
                table: "embeddings",
                columns: new[] { "KindFamily", "Modality" });

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_SourceKey",
                table: "embeddings",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_SourceRunId",
                table: "embeddings",
                column: "SourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_faces_Ignored",
                table: "faces",
                column: "Ignored");

            migrationBuilder.CreateIndex(
                name: "IX_faces_Label",
                table: "faces",
                column: "Label");

            migrationBuilder.CreateIndex(
                name: "IX_faces_MergedIntoFaceId",
                table: "faces",
                column: "MergedIntoFaceId");

            migrationBuilder.CreateIndex(
                name: "IX_faces_PerformerId",
                table: "faces",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_faces_PrimarySourceKey",
                table: "faces",
                column: "PrimarySourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_HostType_HostId_At",
                table: "interactions",
                columns: new[] { "UserId", "HostType", "HostId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_SessionId_Kind_At",
                table: "interactions",
                columns: new[] { "UserId", "SessionId", "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_HostType_HostId_Aspect",
                table: "ratings",
                columns: new[] { "HostType", "HostId", "Aspect" });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_UserId_HostType_HostId_Aspect",
                table: "ratings",
                columns: new[] { "UserId", "HostType", "HostId", "Aspect" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_entity_affinities_UserId_HostType_HostId",
                table: "user_entity_affinities",
                columns: new[] { "UserId", "HostType", "HostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_entity_affinities_UserId_IsFavorite",
                table: "user_entity_affinities",
                columns: new[] { "UserId", "IsFavorite" });

            migrationBuilder.CreateIndex(
                name: "IX_user_entity_affinities_UserId_LastConsumedAt",
                table: "user_entity_affinities",
                columns: new[] { "UserId", "LastConsumedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_runs");

            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "faces");

            migrationBuilder.DropTable(
                name: "interactions");

            migrationBuilder.DropTable(
                name: "ratings");

            migrationBuilder.DropTable(
                name: "user_entity_affinities");
        }
    }
}
