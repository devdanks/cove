using System;
using System.Collections.Generic;
using System.Text.Json;
using Cove.Data.Auth;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class V1_0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS public;");

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
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    ActorKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityTypes = table.Column<string[]>(type: "text[]", nullable: false),
                    Options = table.Column<string[]>(type: "text[]", nullable: false),
                    Filterable = table.Column<bool>(type: "boolean", nullable: false),
                    Sortable = table.Column<bool>(type: "boolean", nullable: false),
                    IsMultiValue = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_definitions", x => x.Id);
                });

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
                name: "entity_identifiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Scheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_identifiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_data",
                columns: table => new
                {
                    ExtensionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_data", x => new { x.ExtensionId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "field_provenance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ModelKey = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_provenance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Path = table.Column<string>(type: "text", nullable: false),
                    ParentFolderId = table.Column<int>(type: "integer", nullable: true),
                    ZipFileId = table.Column<int>(type: "integer", nullable: true),
                    ModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_folders_folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    Meta = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "performers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Disambiguation = table.Column<string>(type: "text", nullable: true),
                    Gender = table.Column<int>(type: "integer", nullable: true),
                    Birthdate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeathDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Ethnicity = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    EyeColor = table.Column<string>(type: "text", nullable: true),
                    HairColor = table.Column<string>(type: "text", nullable: true),
                    HeightCm = table.Column<int>(type: "integer", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: true),
                    Measurements = table.Column<string>(type: "text", nullable: true),
                    FakeTits = table.Column<string>(type: "text", nullable: true),
                    PenisLength = table.Column<double>(type: "double precision", nullable: true),
                    Circumcised = table.Column<int>(type: "integer", nullable: true),
                    CareerStart = table.Column<DateOnly>(type: "date", nullable: true),
                    CareerEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Tattoos = table.Column<string>(type: "text", nullable: true),
                    Piercings = table.Column<string>(type: "text", nullable: true),
                    Favorite = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IgnoreAutoTag = table.Column<bool>(type: "boolean", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GalleryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TagCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Disambiguation\", '') || ' ' || coalesce(\"Details\", '') || ' ' || coalesce(\"SearchText\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Country\", '') || ' ' || coalesce(\"Ethnicity\", '') || ' ' || coalesce(\"Tattoos\", '') || ' ' || coalesce(\"Piercings\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_performers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dangerous = table.Column<bool>(type: "boolean", nullable: false),
                    Implies = table.Column<string>(type: "jsonb", nullable: false),
                    IsOrphaned = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.Key);
                });

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
                    MediaDurationSec = table.Column<double>(type: "double precision", nullable: false),
                    LastPositionSec = table.Column<double>(type: "double precision", nullable: true),
                    TotalWatchedSec = table.Column<double>(type: "double precision", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CountsAsView = table.Column<bool>(type: "boolean", nullable: false),
                    DerivedLikeAwarded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSessions", x => x.Id);
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
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsBuiltin = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_filters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FindFilter = table.Column<string>(type: "jsonb", nullable: true),
                    ObjectFilter = table.Column<string>(type: "jsonb", nullable: true),
                    UIOptions = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_filters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scrape_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScraperId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    InputKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CandidateResultsJson = table.Column<string>(type: "text", nullable: true),
                    EntitySnapshotJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedByUser = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scrape_attempts", x => x.Id);
                });

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
                name: "studios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Favorite = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IgnoreAutoTag = table.Column<bool>(type: "boolean", nullable: false),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GalleryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GroupCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PerformerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ChildStudioCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TagCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_studios_studios_ParentId",
                        column: x => x.ParentId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tag_groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_bookmarks",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_bookmarks", x => new { x.UserId, x.HostType, x.HostId });
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
                    TotalConsumedSec = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    LastPositionSec = table.Column<double>(type: "double precision", nullable: true),
                    LastConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LikeCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DerivedLikeCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PageVisitCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_entity_affinities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PasswordAlgo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    TotpSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UiPreferencesJson = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_field_values",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DefinitionId = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TextValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NumberValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    BoolValue = table.Column<bool>(type: "boolean", nullable: true),
                    DateValue = table.Column<DateOnly>(type: "date", nullable: true),
                    TimestampValue = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntegerValue = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_field_values_custom_field_definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "custom_field_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    AppearanceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FrameSampleCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PrimarySourceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Label\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"PrimarySourceKey\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')", stored: true),
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
                name: "PerformerAlias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformerAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformerAlias_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PerformerRemoteId",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    RemoteId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformerRemoteId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformerRemoteId_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PerformerUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformerUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformerUrl_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "role_content_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Effect = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeValue = table.Column<string>(type: "jsonb", nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_content_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_content_rules_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_entity_overrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Effect = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AppliesTo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_entity_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_entity_overrides_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_PermissionKey",
                        column: x => x.PermissionKey,
                        principalTable: "permissions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    TagIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerformerIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxDuration = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    MaxBitRate = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileSize = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MinPath = table.Column<string>(type: "text", nullable: true),
                    MaxPath = table.Column<string>(type: "text", nullable: true),
                    FileSearchText = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    HasVideoFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audios_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "galleries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Photographer = table.Column<string>(type: "text", nullable: true),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    FolderId = table.Column<int>(type: "integer", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    CoverImageId = table.Column<int>(type: "integer", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    TagIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerformerIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PerformerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TagCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Photographer\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"SearchText\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_galleries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_galleries_folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_galleries_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    QuerySourceKey = table.Column<string>(type: "text", nullable: true),
                    QueryJson = table.Column<string>(type: "text", nullable: true),
                    LastResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CachedItemCount = table.Column<int>(type: "integer", nullable: true),
                    CacheTtlSec = table.Column<int>(type: "integer", nullable: false),
                    ShowInSceneLists = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    AllowedHostTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Aliases = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    Director = table.Column<string>(type: "text", nullable: true),
                    Synopsis = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    FrontImageBlobId = table.Column<string>(type: "text", nullable: true),
                    BackImageBlobId = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"Aliases\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Synopsis\", '') || ' ' || coalesce(\"Director\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_groups_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Photographer = table.Column<string>(type: "text", nullable: true),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    TagIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerformerIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    TagCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PerformerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GalleryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FileCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxResolution = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxFileSize = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MinPath = table.Column<string>(type: "text", nullable: true),
                    MaxPath = table.Column<string>(type: "text", nullable: true),
                    FileSearchText = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    HasDimensionData = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasLandscapeFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasPortraitFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasSquareFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Photographer\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_images_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scenes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Director = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    Captions = table.Column<string>(type: "text", nullable: true),
                    InteractiveSpeed = table.Column<int>(type: "integer", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    ParentSceneId = table.Column<int>(type: "integer", nullable: true),
                    ClipStartSec = table.Column<double>(type: "double precision", nullable: true),
                    ClipEndSec = table.Column<double>(type: "double precision", nullable: true),
                    TagIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerformerIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxDuration = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    MaxResolution = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxHeight = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxFrameRate = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    MaxBitRate = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileSize = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MinPath = table.Column<string>(type: "text", nullable: true),
                    MaxPath = table.Column<string>(type: "text", nullable: true),
                    FileSearchText = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    HasDimensionData = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasLandscapeFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasPortraitFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasSquareFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasInteractiveFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HasNonInteractiveFiles = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '') || ' ' || coalesce(\"Director\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Captions\", '') || ' ' || coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scenes_scenes_ParentSceneId",
                        column: x => x.ParentSceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenes_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudioAlias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudioId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioAlias_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudioRemoteId",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudioId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    RemoteId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioRemoteId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioRemoteId_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudioUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudioId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioUrl_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "text_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Organized = table.Column<bool>(type: "boolean", nullable: false),
                    StudioId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    TagIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerformerIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxWordCount = table.Column<int>(type: "integer", nullable: true),
                    MaxPageCount = table.Column<int>(type: "integer", nullable: true),
                    MaxFileSize = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    MaxFileModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MinPath = table.Column<string>(type: "text", nullable: true),
                    MaxPath = table.Column<string>(type: "text", nullable: true),
                    FileSearchText = table.Column<string>(type: "text", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Title\", '') || ' ' || coalesce(\"Code\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Details\", '')), 'B') ||\r\nsetweight(to_tsvector('simple', coalesce(\"FileSearchText\", '') || ' ' || coalesce(\"SearchText\", '')), 'C')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_text_documents_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortName = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    TagGroupId = table.Column<int>(type: "integer", nullable: true),
                    Favorite = table.Column<bool>(type: "boolean", nullable: false),
                    IgnoreAutoTag = table.Column<bool>(type: "boolean", nullable: false),
                    MinOccurrenceSec = table.Column<double>(type: "double precision", nullable: true),
                    MinOccurrencePercent = table.Column<double>(type: "double precision", nullable: true),
                    ShowAsSegment = table.Column<bool>(type: "boolean", nullable: true),
                    SegmentColorOverride = table.Column<string>(type: "text", nullable: true),
                    SegmentLaneOverride = table.Column<int>(type: "integer", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: true),
                    ImageBlobId = table.Column<string>(type: "text", nullable: true),
                    SceneCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SceneMarkerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ImageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GalleryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    GroupCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PerformerCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StudioCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('simple', coalesce(\"Name\", '') || ' ' || coalesce(\"SortName\", '')), 'A') ||\r\nsetweight(to_tsvector('simple', coalesce(\"Description\", '') || ' ' || coalesce(\"SearchText\", '')), 'B')", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_tag_groups_TagGroupId",
                        column: x => x.TagGroupId,
                        principalTable: "tag_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "api_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScopePermissions = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_refresh_tokens_ParentId",
                        column: x => x.ParentId,
                        principalTable: "refresh_tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "share_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityIds = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_share_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_share_links_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_invite_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    RolesJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_invite_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_invite_tokens_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_invite_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role_assignments",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_role_assignments", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_role_assignments_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_role_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "face_suggestion_decisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FaceId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_face_suggestion_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_face_suggestion_decisions_faces_FaceId",
                        column: x => x.FaceId,
                        principalTable: "faces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_face_suggestion_decisions_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audio_performers",
                columns: table => new
                {
                    AudioId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_performers", x => new { x.AudioId, x.PerformerId });
                    table.ForeignKey(
                        name: "FK_audio_performers_audios_AudioId",
                        column: x => x.AudioId,
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_audio_performers_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audio_tracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AudioId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    StartSec = table.Column<double>(type: "double precision", nullable: false),
                    EndSec = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audio_tracks_audios_AudioId",
                        column: x => x.AudioId,
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audio_urls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AudioId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_urls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audio_urls_audios_AudioId",
                        column: x => x.AudioId,
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gallery_chapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ImageIndex = table.Column<int>(type: "integer", nullable: false),
                    GalleryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gallery_chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gallery_chapters_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gallery_performers",
                columns: table => new
                {
                    GalleryId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gallery_performers", x => new { x.GalleryId, x.PerformerId });
                    table.ForeignKey(
                        name: "FK_gallery_performers_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gallery_performers_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GalleryUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GalleryId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GalleryUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GalleryUrl_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_relations",
                columns: table => new
                {
                    ContainingGroupId = table.Column<int>(type: "integer", nullable: false),
                    SubGroupId = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_relations", x => new { x.ContainingGroupId, x.SubGroupId });
                    table.ForeignKey(
                        name: "FK_group_relations_groups_ContainingGroupId",
                        column: x => x.ContainingGroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_relations_groups_SubGroupId",
                        column: x => x.SubGroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupUrl_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "image_galleries",
                columns: table => new
                {
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    GalleryId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_galleries", x => new { x.ImageId, x.GalleryId });
                    table.ForeignKey(
                        name: "FK_image_galleries_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_galleries_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "image_performers",
                columns: table => new
                {
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_performers", x => new { x.ImageId, x.PerformerId });
                    table.ForeignKey(
                        name: "FK_image_performers_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_performers_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImageUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageUrl_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    HostType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "scene"),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    SceneId = table.Column<int>(type: "integer", nullable: true),
                    ImageId = table.Column<int>(type: "integer", nullable: true),
                    ChildGroupId = table.Column<int>(type: "integer", nullable: true),
                    StartSec = table.Column<double>(type: "double precision", nullable: true),
                    EndSec = table.Column<double>(type: "double precision", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SourceSpanKey = table.Column<string>(type: "text", nullable: true),
                    SourceProfileId = table.Column<int>(type: "integer", nullable: true),
                    SourceQueryJson = table.Column<string>(type: "text", nullable: true),
                    SnapshotAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_group_items_groups_ChildGroupId",
                        column: x => x.ChildGroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_items_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_items_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_items_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_galleries",
                columns: table => new
                {
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    GalleryId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_galleries", x => new { x.SceneId, x.GalleryId });
                    table.ForeignKey(
                        name: "FK_scene_galleries_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_galleries_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_performers",
                columns: table => new
                {
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_performers", x => new { x.SceneId, x.PerformerId });
                    table.ForeignKey(
                        name: "FK_scene_performers_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_performers_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneLikeHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneLikeHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneLikeHistory_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScenePlayHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    PlayedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenePlayHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScenePlayHistory_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneRemoteId",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    RemoteId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneRemoteId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneRemoteId_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneUrl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneUrl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneUrl_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Basename = table.Column<string>(type: "text", nullable: false),
                    ParentFolderId = table.Column<int>(type: "integer", nullable: false),
                    ZipFileId = table.Column<int>(type: "integer", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ModTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    Format = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: true),
                    AudioCodec = table.Column<string>(type: "text", nullable: true),
                    BitRate = table.Column<long>(type: "bigint", nullable: true),
                    SampleRate = table.Column<int>(type: "integer", nullable: true),
                    Channels = table.Column<int>(type: "integer", nullable: true),
                    HasVideoTrack = table.Column<bool>(type: "boolean", nullable: true),
                    AudioId = table.Column<int>(type: "integer", nullable: true),
                    GalleryId = table.Column<int>(type: "integer", nullable: true),
                    ImageFile_Format = table.Column<string>(type: "text", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    ImageId = table.Column<int>(type: "integer", nullable: true),
                    TextFile_Format = table.Column<string>(type: "text", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    ExcerptText = table.Column<string>(type: "text", nullable: true),
                    TextDocumentId = table.Column<int>(type: "integer", nullable: true),
                    VideoFile_Format = table.Column<string>(type: "text", nullable: true),
                    VideoFile_Width = table.Column<int>(type: "integer", nullable: true),
                    VideoFile_Height = table.Column<int>(type: "integer", nullable: true),
                    VideoFile_Duration = table.Column<double>(type: "double precision", nullable: true),
                    VideoCodec = table.Column<string>(type: "text", nullable: true),
                    VideoFile_AudioCodec = table.Column<string>(type: "text", nullable: true),
                    FrameRate = table.Column<double>(type: "double precision", nullable: true),
                    VideoFile_BitRate = table.Column<long>(type: "bigint", nullable: true),
                    Interactive = table.Column<bool>(type: "boolean", nullable: true),
                    InteractiveSpeed = table.Column<int>(type: "integer", nullable: true),
                    SceneId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_files_audios_AudioId",
                        column: x => x.AudioId,
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_files_folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_files_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_files_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_files_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_files_text_documents_TextDocumentId",
                        column: x => x.TextDocumentId,
                        principalTable: "text_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "text_performers",
                columns: table => new
                {
                    TextDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PerformerId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_performers", x => new { x.TextDocumentId, x.PerformerId });
                    table.ForeignKey(
                        name: "FK_text_performers_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_text_performers_text_documents_TextDocumentId",
                        column: x => x.TextDocumentId,
                        principalTable: "text_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "text_urls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TextDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_urls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_text_urls_text_documents_TextDocumentId",
                        column: x => x.TextDocumentId,
                        principalTable: "text_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audio_tags",
                columns: table => new
                {
                    AudioId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_tags", x => new { x.AudioId, x.TagId });
                    table.ForeignKey(
                        name: "FK_audio_tags_audios_AudioId",
                        column: x => x.AudioId,
                        principalTable: "audios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_audio_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gallery_tags",
                columns: table => new
                {
                    GalleryId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gallery_tags", x => new { x.GalleryId, x.TagId });
                    table.ForeignKey(
                        name: "FK_gallery_tags_galleries_GalleryId",
                        column: x => x.GalleryId,
                        principalTable: "galleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gallery_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_tags",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_tags", x => new { x.GroupId, x.TagId });
                    table.ForeignKey(
                        name: "FK_group_tags_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "image_tags",
                columns: table => new
                {
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_tags", x => new { x.ImageId, x.TagId });
                    table.ForeignKey(
                        name: "FK_image_tags_images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "performer_tags",
                columns: table => new
                {
                    PerformerId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_performer_tags", x => new { x.PerformerId, x.TagId });
                    table.ForeignKey(
                        name: "FK_performer_tags_performers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "performers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_performer_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_markers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Seconds = table.Column<double>(type: "double precision", nullable: false),
                    EndSeconds = table.Column<double>(type: "double precision", nullable: true),
                    PrimaryTagId = table.Column<int>(type: "integer", nullable: false),
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_markers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scene_markers_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_markers_tags_PrimaryTagId",
                        column: x => x.PrimaryTagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_tags",
                columns: table => new
                {
                    SceneId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_tags", x => new { x.SceneId, x.TagId });
                    table.ForeignKey(
                        name: "FK_scene_tags_scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "segment_display_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<int>(type: "integer", nullable: false),
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
                        name: "FK_segment_display_rules_segment_display_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "segment_display_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "studio_tags",
                columns: table => new
                {
                    StudioId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_tags", x => new { x.StudioId, x.TagId });
                    table.ForeignKey(
                        name: "FK_studio_tags_studios_StudioId",
                        column: x => x.StudioId,
                        principalTable: "studios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_studio_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostType = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ContextType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ContextId = table.Column<int>(type: "integer", nullable: true),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    SourceRunId = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    ModelKey = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    TotalDurationSec = table.Column<double>(type: "double precision", nullable: true),
                    HostDurationSec = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tag_applications_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_parents",
                columns: table => new
                {
                    ParentId = table.Column<int>(type: "integer", nullable: false),
                    ChildId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_parents", x => new { x.ParentId, x.ChildId });
                    table.ForeignKey(
                        name: "FK_tag_parents_tags_ChildId",
                        column: x => x.ChildId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tag_parents_tags_ParentId",
                        column: x => x.ParentId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagAlias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagAlias_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagRemoteId",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    RemoteId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagRemoteId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagRemoteId_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "text_tags",
                columns: table => new
                {
                    TextDocumentId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_tags", x => new { x.TextDocumentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_text_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_text_tags_text_documents_TextDocumentId",
                        column: x => x.TextDocumentId,
                        principalTable: "text_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileFingerprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileFingerprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileFingerprints_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoCaptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    CaptionType = table.Column<string>(type: "text", nullable: false),
                    Filename = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoCaptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoCaptions_files_FileId",
                        column: x => x.FileId,
                        principalTable: "files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scene_marker_tags",
                columns: table => new
                {
                    SceneMarkerId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_marker_tags", x => new { x.SceneMarkerId, x.TagId });
                    table.ForeignKey(
                        name: "FK_scene_marker_tags_scene_markers_SceneMarkerId",
                        column: x => x.SceneMarkerId,
                        principalTable: "scene_markers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scene_marker_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "IX_api_tokens_TokenHash",
                table: "api_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_tokens_UserId",
                table: "api_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_audio_performers_PerformerId",
                table: "audio_performers",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_audio_tags_TagId",
                table: "audio_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_audio_tracks_AudioId_OrderIndex",
                table: "audio_tracks",
                columns: new[] { "AudioId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_audio_urls_AudioId",
                table: "audio_urls",
                column: "AudioId");

            migrationBuilder.CreateIndex(
                name: "IX_audios_CreatedAt",
                table: "audios",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_audios_Date",
                table: "audios",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_audios_MaxDuration",
                table: "audios",
                column: "MaxDuration");

            migrationBuilder.CreateIndex(
                name: "IX_audios_PerformerIds",
                table: "audios",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_audios_SearchVector",
                table: "audios",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_audios_StudioId",
                table: "audios",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_audios_TagIds",
                table: "audios",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_audios_Title",
                table: "audios",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_audios_UpdatedAt",
                table: "audios",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_Action_OccurredAt",
                table: "audit_events",
                columns: new[] { "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorUserId_OccurredAt",
                table: "audit_events",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OccurredAt",
                table: "audit_events",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_DisplayOrder",
                table: "custom_field_definitions",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_Key",
                table: "custom_field_definitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_BoolValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "BoolValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_DateValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "DateValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_EntityId_Positi~",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "EntityId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_IntegerValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "IntegerValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_NumberValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "NumberValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_TextValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "TextValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_DefinitionId_EntityType_TimestampValue",
                table: "custom_field_values",
                columns: new[] { "DefinitionId", "EntityType", "TimestampValue" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_values_EntityType_EntityId",
                table: "custom_field_values",
                columns: new[] { "EntityType", "EntityId" });

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
                name: "IX_entity_identifiers_EntityKind_EntityId_Scheme_NormalizedVal~",
                table: "entity_identifiers",
                columns: new[] { "EntityKind", "EntityId", "Scheme", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_identifiers_Scheme_NormalizedValue",
                table: "entity_identifiers",
                columns: new[] { "Scheme", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_extension_data_ExtensionId",
                table: "extension_data",
                column: "ExtensionId");

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

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_FaceId_PerformerId_UserId",
                table: "face_suggestion_decisions",
                columns: new[] { "FaceId", "PerformerId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_FaceId_UserId",
                table: "face_suggestion_decisions",
                columns: new[] { "FaceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_PerformerId",
                table: "face_suggestion_decisions",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_face_suggestion_decisions_UserId_Decision",
                table: "face_suggestion_decisions",
                columns: new[] { "UserId", "Decision" });

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
                name: "IX_faces_SearchVector",
                table: "faces",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId_FieldKey",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_HostType_HostId_FieldKey_SourceKey_SourceR~",
                table: "field_provenance",
                columns: new[] { "HostType", "HostId", "FieldKey", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_field_provenance_SourceKey",
                table: "field_provenance",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_FileFingerprints_FileId",
                table: "FileFingerprints",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileFingerprints_Type_Value",
                table: "FileFingerprints",
                columns: new[] { "Type", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_files_AudioId_Path",
                table: "files",
                columns: new[] { "AudioId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_GalleryId_Path",
                table: "files",
                columns: new[] { "GalleryId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_ImageId_Path",
                table: "files",
                columns: new[] { "ImageId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_ParentFolderId_Basename",
                table: "files",
                columns: new[] { "ParentFolderId", "Basename" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_files_Path",
                table: "files",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_files_SceneId_Path",
                table: "files",
                columns: new[] { "SceneId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_files_TextDocumentId_Path",
                table: "files",
                columns: new[] { "TextDocumentId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_folders_ParentFolderId",
                table: "folders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_folders_Path",
                table: "folders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_galleries_CreatedAt",
                table: "galleries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Date",
                table: "galleries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_FolderId",
                table: "galleries",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_ImageCount",
                table: "galleries",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Organized",
                table: "galleries",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_PerformerCount",
                table: "galleries",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_PerformerIds",
                table: "galleries",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_SceneCount",
                table: "galleries",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_SearchVector",
                table: "galleries",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_StudioId",
                table: "galleries",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_TagCount",
                table: "galleries",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_TagIds",
                table: "galleries",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_Title",
                table: "galleries",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_galleries_UpdatedAt",
                table: "galleries",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_chapters_GalleryId",
                table: "gallery_chapters",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_performers_PerformerId",
                table: "gallery_performers",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_tags_TagId",
                table: "gallery_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_GalleryUrl_GalleryId",
                table: "GalleryUrl",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_ChildGroupId",
                table: "group_items",
                column: "ChildGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_GroupId_OrderIndex",
                table: "group_items",
                columns: new[] { "GroupId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_HostType_HostId",
                table: "group_items",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_group_items_ImageId",
                table: "group_items",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_SceneId",
                table: "group_items",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_group_items_SourceProfileId",
                table: "group_items",
                column: "SourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_group_relations_SubGroupId",
                table: "group_relations",
                column: "SubGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_group_tags_TagId",
                table: "group_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_Name",
                table: "groups",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_groups_SearchVector",
                table: "groups",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_groups_SortOrder",
                table: "groups",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_groups_StudioId",
                table: "groups",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupUrl_GroupId",
                table: "GroupUrl",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_image_galleries_GalleryId",
                table: "image_galleries",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_image_performers_PerformerId",
                table: "image_performers",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_image_tags_TagId",
                table: "image_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_images_CreatedAt",
                table: "images",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_images_FileCount",
                table: "images",
                column: "FileCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_GalleryCount",
                table: "images",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasDimensionData",
                table: "images",
                column: "HasDimensionData");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasLandscapeFiles",
                table: "images",
                column: "HasLandscapeFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasPortraitFiles",
                table: "images",
                column: "HasPortraitFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_HasSquareFiles",
                table: "images",
                column: "HasSquareFiles");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxFileModTime",
                table: "images",
                column: "MaxFileModTime");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxFileSize",
                table: "images",
                column: "MaxFileSize");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxPath",
                table: "images",
                column: "MaxPath");

            migrationBuilder.CreateIndex(
                name: "IX_images_MaxResolution",
                table: "images",
                column: "MaxResolution");

            migrationBuilder.CreateIndex(
                name: "IX_images_MinPath",
                table: "images",
                column: "MinPath");

            migrationBuilder.CreateIndex(
                name: "IX_images_Organized",
                table: "images",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_images_PerformerCount",
                table: "images",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_PerformerIds",
                table: "images",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_SearchVector",
                table: "images",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_StudioId",
                table: "images",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_images_TagCount",
                table: "images",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_images_TagIds",
                table: "images",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_images_Title",
                table: "images",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_images_UpdatedAt",
                table: "images",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImageUrl_ImageId",
                table: "ImageUrl",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_HostType_HostId_At",
                table: "interactions",
                columns: new[] { "UserId", "HostType", "HostId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_performer_tags_TagId",
                table: "performer_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformerAlias_PerformerId",
                table: "PerformerAlias",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformerRemoteId_PerformerId",
                table: "PerformerRemoteId",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_performers_Favorite",
                table: "performers",
                column: "Favorite");

            migrationBuilder.CreateIndex(
                name: "IX_performers_GalleryCount",
                table: "performers",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_ImageCount",
                table: "performers",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_Name",
                table: "performers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_performers_SceneCount",
                table: "performers",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_performers_SearchVector",
                table: "performers",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_performers_TagCount",
                table: "performers",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_PerformerUrl_PerformerId",
                table: "PerformerUrl",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_Source",
                table: "permissions",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackIntervals_PlaybackSessionId_StartSec",
                table: "PlaybackIntervals",
                columns: new[] { "PlaybackSessionId", "StartSec" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackIntervals_UserId_HostType_HostId",
                table: "PlaybackIntervals",
                columns: new[] { "UserId", "HostType", "HostId" });

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
                name: "IX_ratings_HostType_HostId_Aspect",
                table: "ratings",
                columns: new[] { "HostType", "HostId", "Aspect" });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_UserId_HostType_HostId_Aspect",
                table: "ratings",
                columns: new[] { "UserId", "HostType", "HostId", "Aspect" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_ParentId",
                table: "refresh_tokens",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_RevokedAt",
                table: "refresh_tokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_role_content_rules_RoleId_EntityKind_AppliesTo",
                table: "role_content_rules",
                columns: new[] { "RoleId", "EntityKind", "AppliesTo" });

            migrationBuilder.CreateIndex(
                name: "IX_role_entity_overrides_EntityKind_EntityId_AppliesTo",
                table: "role_entity_overrides",
                columns: new[] { "EntityKind", "EntityId", "AppliesTo" });

            migrationBuilder.CreateIndex(
                name: "IX_role_entity_overrides_RoleId_EntityKind_EntityId_AppliesTo",
                table: "role_entity_overrides",
                columns: new[] { "RoleId", "EntityKind", "EntityId", "AppliesTo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_PermissionKey",
                table: "role_permissions",
                column: "PermissionKey");

            migrationBuilder.CreateIndex(
                name: "IX_roles_Name",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scene_galleries_GalleryId",
                table: "scene_galleries",
                column: "GalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_scene_marker_tags_TagId",
                table: "scene_marker_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_scene_markers_PrimaryTagId",
                table: "scene_markers",
                column: "PrimaryTagId");

            migrationBuilder.CreateIndex(
                name: "IX_scene_markers_SceneId",
                table: "scene_markers",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_scene_performers_PerformerId",
                table: "scene_performers",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_scene_tags_TagId",
                table: "scene_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneLikeHistory_SceneId",
                table: "SceneLikeHistory",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_ScenePlayHistory_SceneId",
                table: "ScenePlayHistory",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneRemoteId_SceneId",
                table: "SceneRemoteId",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_CreatedAt",
                table: "scenes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_Date",
                table: "scenes",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_FileCount",
                table: "scenes",
                column: "FileCount");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasDimensionData",
                table: "scenes",
                column: "HasDimensionData");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasInteractiveFiles",
                table: "scenes",
                column: "HasInteractiveFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasLandscapeFiles",
                table: "scenes",
                column: "HasLandscapeFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasNonInteractiveFiles",
                table: "scenes",
                column: "HasNonInteractiveFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasPortraitFiles",
                table: "scenes",
                column: "HasPortraitFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_HasSquareFiles",
                table: "scenes",
                column: "HasSquareFiles");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxBitRate",
                table: "scenes",
                column: "MaxBitRate");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxDuration",
                table: "scenes",
                column: "MaxDuration");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFileModTime",
                table: "scenes",
                column: "MaxFileModTime");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFileSize",
                table: "scenes",
                column: "MaxFileSize");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxFrameRate",
                table: "scenes",
                column: "MaxFrameRate");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxHeight",
                table: "scenes",
                column: "MaxHeight");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxPath",
                table: "scenes",
                column: "MaxPath");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MaxResolution",
                table: "scenes",
                column: "MaxResolution");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_MinPath",
                table: "scenes",
                column: "MinPath");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_Organized",
                table: "scenes",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_ParentSceneId",
                table: "scenes",
                column: "ParentSceneId");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_PerformerIds",
                table: "scenes",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_SearchVector",
                table: "scenes",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_StudioId",
                table: "scenes",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_TagIds",
                table: "scenes",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_Title",
                table: "scenes",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_scenes_UpdatedAt",
                table: "scenes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SceneUrl_SceneId",
                table: "SceneUrl",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_CreatedAt",
                table: "scrape_attempts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_EntityType_EntityId_CreatedAt",
                table: "scrape_attempts",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scrape_attempts_Status",
                table: "scrape_attempts",
                column: "Status");

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

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_ProfileId",
                table: "segment_display_rules",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_segment_display_rules_ProfileId_SourceKey_Kind_TagId_TagCat~",
                table: "segment_display_rules",
                columns: new[] { "ProfileId", "SourceKey", "Kind", "TagId", "TagCategory", "HostType", "Priority" });

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

            migrationBuilder.CreateIndex(
                name: "IX_share_links_CreatedByUserId",
                table: "share_links",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_share_links_TokenHash",
                table: "share_links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_studio_tags_TagId",
                table: "studio_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioAlias_StudioId",
                table: "StudioAlias",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioRemoteId_StudioId",
                table: "StudioRemoteId",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_studios_ChildStudioCount",
                table: "studios",
                column: "ChildStudioCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Favorite",
                table: "studios",
                column: "Favorite");

            migrationBuilder.CreateIndex(
                name: "IX_studios_GalleryCount",
                table: "studios",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_GroupCount",
                table: "studios",
                column: "GroupCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_ImageCount",
                table: "studios",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Name",
                table: "studios",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_studios_Organized",
                table: "studios",
                column: "Organized");

            migrationBuilder.CreateIndex(
                name: "IX_studios_ParentId",
                table: "studios",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_studios_PerformerCount",
                table: "studios",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_SceneCount",
                table: "studios",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_studios_SearchVector",
                table: "studios",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_studios_TagCount",
                table: "studios",
                column: "TagCount");

            migrationBuilder.CreateIndex(
                name: "IX_StudioUrl_StudioId",
                table: "StudioUrl",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "ContextType", "ContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_ContextType_ContextId_TagI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "ContextType", "ContextId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true,
                filter: "\"ContextType\" IS NOT NULL AND \"ContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_HostType_HostId_TagId_SourceKey_SourceRunI~",
                table: "tag_applications",
                columns: new[] { "HostType", "HostId", "TagId", "SourceKey", "SourceRunId", "ModelKey" },
                unique: true,
                filter: "\"ContextType\" IS NULL AND \"ContextId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_SourceKey",
                table: "tag_applications",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_tag_applications_TagId",
                table: "tag_applications",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_tag_groups_Name",
                table: "tag_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tag_groups_SortOrder",
                table: "tag_groups",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_tag_parents_ChildId",
                table: "tag_parents",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_TagAlias_TagId",
                table: "TagAlias",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagRemoteId_TagId",
                table: "TagRemoteId",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Favorite",
                table: "tags",
                column: "Favorite");

            migrationBuilder.CreateIndex(
                name: "IX_tags_GalleryCount",
                table: "tags",
                column: "GalleryCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_GroupCount",
                table: "tags",
                column: "GroupCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_ImageCount",
                table: "tags",
                column: "ImageCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Name",
                table: "tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_PerformerCount",
                table: "tags",
                column: "PerformerCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SceneCount",
                table: "tags",
                column: "SceneCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SceneMarkerCount",
                table: "tags",
                column: "SceneMarkerCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_SearchVector",
                table: "tags",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_tags_StudioCount",
                table: "tags",
                column: "StudioCount");

            migrationBuilder.CreateIndex(
                name: "IX_tags_TagGroupId",
                table: "tags",
                column: "TagGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_CreatedAt",
                table: "text_documents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_Date",
                table: "text_documents",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_PerformerIds",
                table: "text_documents",
                column: "PerformerIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_SearchVector",
                table: "text_documents",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_StudioId",
                table: "text_documents",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_TagIds",
                table: "text_documents",
                column: "TagIds")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_Title",
                table: "text_documents",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_text_documents_UpdatedAt",
                table: "text_documents",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_text_performers_PerformerId",
                table: "text_performers",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_text_tags_TagId",
                table: "text_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_text_urls_TextDocumentId",
                table: "text_urls",
                column: "TextDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_user_bookmarks_UserId_CreatedAt",
                table: "user_bookmarks",
                columns: new[] { "UserId", "CreatedAt" });

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

            migrationBuilder.CreateIndex(
                name: "IX_user_invite_tokens_CreatedByUserId",
                table: "user_invite_tokens",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_invite_tokens_TokenHash",
                table: "user_invite_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_invite_tokens_UserId_Purpose_ConsumedAt",
                table: "user_invite_tokens",
                columns: new[] { "UserId", "Purpose", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_GrantedByUserId",
                table: "user_role_assignments",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_RoleId",
                table: "user_role_assignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoCaptions_FileId",
                table: "VideoCaptions",
                column: "FileId");

            migrationBuilder.Sql(AuthorizationSqlDefinitions.CreateFunctionsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuthorizationSqlDefinitions.DropFunctionsSql);

            migrationBuilder.DropTable(
                name: "ai_runs");

            migrationBuilder.DropTable(
                name: "api_tokens");

            migrationBuilder.DropTable(
                name: "audio_performers");

            migrationBuilder.DropTable(
                name: "audio_tags");

            migrationBuilder.DropTable(
                name: "audio_tracks");

            migrationBuilder.DropTable(
                name: "audio_urls");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "custom_field_values");

            migrationBuilder.DropTable(
                name: "detections");

            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "entity_identifiers");

            migrationBuilder.DropTable(
                name: "extension_data");

            migrationBuilder.DropTable(
                name: "face_appearances");

            migrationBuilder.DropTable(
                name: "face_suggestion_decisions");

            migrationBuilder.DropTable(
                name: "field_provenance");

            migrationBuilder.DropTable(
                name: "FileFingerprints");

            migrationBuilder.DropTable(
                name: "gallery_chapters");

            migrationBuilder.DropTable(
                name: "gallery_performers");

            migrationBuilder.DropTable(
                name: "gallery_tags");

            migrationBuilder.DropTable(
                name: "GalleryUrl");

            migrationBuilder.DropTable(
                name: "group_items");

            migrationBuilder.DropTable(
                name: "group_relations");

            migrationBuilder.DropTable(
                name: "group_tags");

            migrationBuilder.DropTable(
                name: "GroupUrl");

            migrationBuilder.DropTable(
                name: "image_galleries");

            migrationBuilder.DropTable(
                name: "image_performers");

            migrationBuilder.DropTable(
                name: "image_tags");

            migrationBuilder.DropTable(
                name: "ImageUrl");

            migrationBuilder.DropTable(
                name: "interactions");

            migrationBuilder.DropTable(
                name: "performer_tags");

            migrationBuilder.DropTable(
                name: "PerformerAlias");

            migrationBuilder.DropTable(
                name: "PerformerRemoteId");

            migrationBuilder.DropTable(
                name: "PerformerUrl");

            migrationBuilder.DropTable(
                name: "PlaybackIntervals");

            migrationBuilder.DropTable(
                name: "ratings");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_content_rules");

            migrationBuilder.DropTable(
                name: "role_entity_overrides");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "saved_filters");

            migrationBuilder.DropTable(
                name: "scene_galleries");

            migrationBuilder.DropTable(
                name: "scene_marker_tags");

            migrationBuilder.DropTable(
                name: "scene_performers");

            migrationBuilder.DropTable(
                name: "scene_tags");

            migrationBuilder.DropTable(
                name: "SceneLikeHistory");

            migrationBuilder.DropTable(
                name: "ScenePlayHistory");

            migrationBuilder.DropTable(
                name: "SceneRemoteId");

            migrationBuilder.DropTable(
                name: "SceneUrl");

            migrationBuilder.DropTable(
                name: "scrape_attempts");

            migrationBuilder.DropTable(
                name: "segment_display_rules");

            migrationBuilder.DropTable(
                name: "segments");

            migrationBuilder.DropTable(
                name: "share_links");

            migrationBuilder.DropTable(
                name: "studio_tags");

            migrationBuilder.DropTable(
                name: "StudioAlias");

            migrationBuilder.DropTable(
                name: "StudioRemoteId");

            migrationBuilder.DropTable(
                name: "StudioUrl");

            migrationBuilder.DropTable(
                name: "tag_applications");

            migrationBuilder.DropTable(
                name: "tag_parents");

            migrationBuilder.DropTable(
                name: "TagAlias");

            migrationBuilder.DropTable(
                name: "TagRemoteId");

            migrationBuilder.DropTable(
                name: "text_performers");

            migrationBuilder.DropTable(
                name: "text_tags");

            migrationBuilder.DropTable(
                name: "text_urls");

            migrationBuilder.DropTable(
                name: "user_bookmarks");

            migrationBuilder.DropTable(
                name: "user_entity_affinities");

            migrationBuilder.DropTable(
                name: "user_invite_tokens");

            migrationBuilder.DropTable(
                name: "user_role_assignments");

            migrationBuilder.DropTable(
                name: "VideoCaptions");

            migrationBuilder.DropTable(
                name: "custom_field_definitions");

            migrationBuilder.DropTable(
                name: "faces");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "PlaybackSessions");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "scene_markers");

            migrationBuilder.DropTable(
                name: "segment_display_profiles");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "performers");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "audios");

            migrationBuilder.DropTable(
                name: "galleries");

            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "scenes");

            migrationBuilder.DropTable(
                name: "text_documents");

            migrationBuilder.DropTable(
                name: "tag_groups");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropTable(
                name: "studios");
        }
    }
}
