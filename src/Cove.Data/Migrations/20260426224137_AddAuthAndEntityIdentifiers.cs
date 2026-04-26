using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthAndEntityIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropIndex(
                name: "IX_users_ApiKey",
                table: "users");

            // The legacy single-user "users" table was never populated in shipping installs
            // (auth used Cove:Auth from configuration). Drop the legacy columns and add the
            // new auth model columns with correct semantics + defaults.
            migrationBuilder.DropColumn(name: "ApiKey", table: "users");
            migrationBuilder.DropColumn(name: "IsAdmin", table: "users");
            migrationBuilder.DropColumn(name: "IsEnabled", table: "users");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastLoginIp",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordAlgo",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "bcrypt");

            migrationBuilder.AddColumn<string>(
                name: "TotpSecret",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Note: CandidateResultsJson on scrape_attempts is reflected in the model snapshot
            // but already exists in deployed databases (added out-of-band before this migration).
            // The column is intentionally not (re-)added here to avoid 42701 conflicts.

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

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

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
                name: "IX_entity_identifiers_EntityKind_EntityId_Scheme_NormalizedVal~",
                table: "entity_identifiers",
                columns: new[] { "EntityKind", "EntityId", "Scheme", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entity_identifiers_Scheme_NormalizedValue",
                table: "entity_identifiers",
                columns: new[] { "Scheme", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_permissions_Source",
                table: "permissions",
                column: "Source");

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
                name: "IX_share_links_CreatedByUserId",
                table: "share_links",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_share_links_TokenHash",
                table: "share_links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_GrantedByUserId",
                table: "user_role_assignments",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_role_assignments_RoleId",
                table: "user_role_assignments",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_tokens");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "entity_identifiers");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_content_rules");

            migrationBuilder.DropTable(
                name: "role_entity_overrides");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "share_links");

            migrationBuilder.DropTable(
                name: "user_role_assignments");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LastLoginIp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordAlgo",
                table: "users");

            migrationBuilder.DropColumn(name: "TotpSecret", table: "users");
            migrationBuilder.DropColumn(name: "IsSystem", table: "users");
            migrationBuilder.DropColumn(name: "MustChangePassword", table: "users");

            // CandidateResultsJson on scrape_attempts is intentionally not dropped here
            // (it predates this migration in deployed databases).

            migrationBuilder.AddColumn<string>(
                name: "ApiKey",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_ApiKey",
                table: "users",
                column: "ApiKey",
                unique: true,
                filter: "\"ApiKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_Role",
                table: "user_roles",
                columns: new[] { "UserId", "Role" },
                unique: true);
        }
    }
}
