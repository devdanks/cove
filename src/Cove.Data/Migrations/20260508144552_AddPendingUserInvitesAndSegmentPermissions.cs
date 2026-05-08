using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cove.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingUserInvitesAndSegmentPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "user_invite_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "user_invite_tokens",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RolesJson",
                table: "user_invite_tokens",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "user_invite_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO permissions ("Key", "Category", "Description", "Source", "Dangerous", "Implies", "IsOrphaned", "RegisteredAt")
                SELECT values_table.new_key, 'Segments', values_table.description, COALESCE(old_permission."Source", 'core'), values_table.dangerous, values_table.implies::jsonb, false, NOW()
                FROM (VALUES
                    ('markers.read', 'segments.read', 'View segments and detections.', false, '[]'),
                    ('markers.write', 'segments.write', 'Create or edit segments and detections.', false, '["segments.read"]'),
                    ('markers.delete', 'segments.delete', 'Delete segments and detections.', false, '["segments.read"]')
                ) AS values_table(old_key, new_key, description, dangerous, implies)
                LEFT JOIN permissions old_permission ON old_permission."Key" = values_table.old_key
                WHERE NOT EXISTS (
                    SELECT 1 FROM permissions existing_permission WHERE existing_permission."Key" = values_table.new_key
                );

                INSERT INTO role_permissions ("RoleId", "PermissionKey")
                SELECT role_permission."RoleId", values_table.new_key
                FROM role_permissions role_permission
                JOIN (VALUES
                    ('markers.read', 'segments.read'),
                    ('markers.write', 'segments.write'),
                    ('markers.delete', 'segments.delete')
                ) AS values_table(old_key, new_key) ON role_permission."PermissionKey" = values_table.old_key
                ON CONFLICT ("RoleId", "PermissionKey") DO NOTHING;

                DELETE FROM role_permissions
                WHERE "PermissionKey" IN ('markers.read', 'markers.write', 'markers.delete');

                UPDATE api_tokens
                SET "ScopePermissions" = (
                    SELECT jsonb_agg(
                        CASE scope_value
                            WHEN 'markers.read' THEN 'segments.read'
                            WHEN 'markers.write' THEN 'segments.write'
                            WHEN 'markers.delete' THEN 'segments.delete'
                            ELSE scope_value
                        END
                    )
                    FROM jsonb_array_elements_text("ScopePermissions") AS scope(scope_value)
                )
                WHERE "ScopePermissions" IS NOT NULL
                  AND "ScopePermissions" ?| ARRAY['markers.read', 'markers.write', 'markers.delete'];

                DELETE FROM permissions
                WHERE "Key" IN ('markers.read', 'markers.write', 'markers.delete');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO permissions ("Key", "Category", "Description", "Source", "Dangerous", "Implies", "IsOrphaned", "RegisteredAt")
                SELECT values_table.new_key, 'Segments', values_table.description, COALESCE(old_permission."Source", 'core'), values_table.dangerous, values_table.implies::jsonb, false, NOW()
                FROM (VALUES
                    ('segments.read', 'markers.read', 'View segments and detections.', false, '[]'),
                    ('segments.write', 'markers.write', 'Create or edit segments and detections.', false, '["markers.read"]'),
                    ('segments.delete', 'markers.delete', 'Delete segments and detections.', false, '["markers.read"]')
                ) AS values_table(old_key, new_key, description, dangerous, implies)
                LEFT JOIN permissions old_permission ON old_permission."Key" = values_table.old_key
                WHERE NOT EXISTS (
                    SELECT 1 FROM permissions existing_permission WHERE existing_permission."Key" = values_table.new_key
                );

                INSERT INTO role_permissions ("RoleId", "PermissionKey")
                SELECT role_permission."RoleId", values_table.new_key
                FROM role_permissions role_permission
                JOIN (VALUES
                    ('segments.read', 'markers.read'),
                    ('segments.write', 'markers.write'),
                    ('segments.delete', 'markers.delete')
                ) AS values_table(old_key, new_key) ON role_permission."PermissionKey" = values_table.old_key
                ON CONFLICT ("RoleId", "PermissionKey") DO NOTHING;

                DELETE FROM role_permissions
                WHERE "PermissionKey" IN ('segments.read', 'segments.write', 'segments.delete');

                UPDATE api_tokens
                SET "ScopePermissions" = (
                    SELECT jsonb_agg(
                        CASE scope_value
                            WHEN 'segments.read' THEN 'markers.read'
                            WHEN 'segments.write' THEN 'markers.write'
                            WHEN 'segments.delete' THEN 'markers.delete'
                            ELSE scope_value
                        END
                    )
                    FROM jsonb_array_elements_text("ScopePermissions") AS scope(scope_value)
                )
                WHERE "ScopePermissions" IS NOT NULL
                  AND "ScopePermissions" ?| ARRAY['segments.read', 'segments.write', 'segments.delete'];

                DELETE FROM permissions
                WHERE "Key" IN ('segments.read', 'segments.write', 'segments.delete');
                """);

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "user_invite_tokens");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "user_invite_tokens");

            migrationBuilder.DropColumn(
                name: "RolesJson",
                table: "user_invite_tokens");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "user_invite_tokens");
        }
    }
}
