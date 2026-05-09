using System.Data.Common;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Cove.Api.Startup;

internal static class SchemaCompatibilityBootstrap
{
    private static int _ensureCompatibilitySchemaInvocationCount;

    internal static int EnsureCompatibilitySchemaInvocationCount
        => System.Threading.Volatile.Read(ref _ensureCompatibilitySchemaInvocationCount);

    internal static void ResetTestState()
        => System.Threading.Interlocked.Exchange(ref _ensureCompatibilitySchemaInvocationCount, 0);

    public static async Task EnsureCompatibilitySchemaAsync(CoveContext db)
    {
        System.Threading.Interlocked.Increment(ref _ensureCompatibilitySchemaInvocationCount);

        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return;

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        async Task<int> ExecuteNonQueryAsync(string sql, DbTransaction? transaction = null)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (transaction is not null)
                cmd.Transaction = transaction;
            return await cmd.ExecuteNonQueryAsync();
        }

        async Task<string?> GetRelationKindAsync(string relation)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT c.relkind
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = '{relation}'
                LIMIT 1
                """;
            var kind = await cmd.ExecuteScalarAsync();
            return kind?.ToString();
        }

        async Task AddColumnIfMissing(string table, string column, string type, string? defaultValue = null, bool skipIfTableMissing = false)
        {
            var relationKind = await GetRelationKindAsync(table);
            if (relationKind is not ("r" or "p"))
            {
                if (relationKind is null && skipIfTableMissing)
                    return;

                Log.Warning("Skipping compatibility column bootstrap for relation {Relation} (kind={Kind})", table, relationKind ?? "missing");
                return;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_name='{table}' AND column_name='{column}'";
            var exists = await cmd.ExecuteScalarAsync();
            if (exists != null) return;

            var def = defaultValue != null ? $" DEFAULT {defaultValue}" : string.Empty;
            await using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}{def}";
            await alter.ExecuteNonQueryAsync();
        }

        async Task EnsureTableExists(string table, string createSql)
        {
            var relationKind = await GetRelationKindAsync(table);
            if (relationKind is "r" or "p") return;
            if (relationKind != null)
            {
                Log.Warning("Skipping compatibility table bootstrap for relation {Relation} (kind={Kind})", table, relationKind);
                return;
            }

            await ExecuteNonQueryAsync(createSql);
        }

        async Task EnsureLegacyIdentifierTableAsync(
            string table,
            string parentTable,
            string foreignKeyColumn,
            string valueColumn,
            string entityKind,
            string scheme,
            string createSql,
            string[] indexSql,
            bool includeEndpoint = false)
        {
            var relationKind = await GetRelationKindAsync(table);
            if (relationKind is "r" or "p")
                return;

            if (relationKind is not null && relationKind != "v")
            {
                Log.Warning("Skipping legacy identifier compatibility bootstrap for relation {Relation} (kind={Kind})", table, relationKind);
                return;
            }

            var identifierStoreKind = await GetRelationKindAsync("entity_identifiers");

            await using var transaction = await conn.BeginTransactionAsync();

            if (relationKind == "v")
            {
                Log.Warning("Replacing legacy identifier view {Relation} with a physical table", table);
                await ExecuteNonQueryAsync($"DROP VIEW IF EXISTS \"{table}\"", transaction);
            }

            await ExecuteNonQueryAsync(createSql, transaction);

            if (identifierStoreKind is "r" or "p")
            {
                var endpointColumns = includeEndpoint ? ", \"Endpoint\"" : string.Empty;
                var endpointProjection = includeEndpoint ? ", COALESCE(ei.\"Source\", '')" : string.Empty;
                var insertSql = $"""
                    INSERT INTO "{table}" ("{foreignKeyColumn}"{endpointColumns}, "{valueColumn}")
                    SELECT DISTINCT ei."EntityId"{endpointProjection}, ei."Value"
                    FROM "entity_identifiers" ei
                    JOIN "{parentTable}" parent ON parent."Id" = ei."EntityId"
                    WHERE ei."EntityKind" = '{entityKind}' AND ei."Scheme" = '{scheme}'
                    """;

                var inserted = await ExecuteNonQueryAsync(insertSql, transaction);
                if (inserted > 0)
                    Log.Information("Backfilled {Count} rows into {Relation} from entity_identifiers", inserted, table);
            }

            foreach (var sql in indexSql)
                await ExecuteNonQueryAsync(sql, transaction);

            await transaction.CommitAsync();
        }

        await AddColumnIfMissing("galleries", "ImageBlobId", "text");
        await AddColumnIfMissing("galleries", "CoverImageId", "integer");
        await AddColumnIfMissing("tags", "ShowAsSegment", "boolean");
        await AddColumnIfMissing("tags", "SegmentColorOverride", "text");
        await AddColumnIfMissing("tags", "SegmentLaneOverride", "integer");

        var legacyIdentifierRelations = new[]
        {
            new
            {
                Table = "SceneUrl",
                ParentTable = "scenes",
                ForeignKeyColumn = "SceneId",
                ValueColumn = "Url",
                EntityKind = "scene",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "SceneUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "SceneId" integer NOT NULL REFERENCES "scenes"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_SceneUrl_SceneId\" ON \"SceneUrl\" (\"SceneId\")" }
            },
            new
            {
                Table = "ImageUrl",
                ParentTable = "images",
                ForeignKeyColumn = "ImageId",
                ValueColumn = "Url",
                EntityKind = "image",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "ImageUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "ImageId" integer NOT NULL REFERENCES "images"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_ImageUrl_ImageId\" ON \"ImageUrl\" (\"ImageId\")" }
            },
            new
            {
                Table = "GalleryUrl",
                ParentTable = "galleries",
                ForeignKeyColumn = "GalleryId",
                ValueColumn = "Url",
                EntityKind = "gallery",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "GalleryUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "GalleryId" integer NOT NULL REFERENCES "galleries"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_GalleryUrl_GalleryId\" ON \"GalleryUrl\" (\"GalleryId\")" }
            },
            new
            {
                Table = "GroupUrl",
                ParentTable = "groups",
                ForeignKeyColumn = "GroupId",
                ValueColumn = "Url",
                EntityKind = "group",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "GroupUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "GroupId" integer NOT NULL REFERENCES "groups"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_GroupUrl_GroupId\" ON \"GroupUrl\" (\"GroupId\")" }
            },
            new
            {
                Table = "PerformerUrl",
                ParentTable = "performers",
                ForeignKeyColumn = "PerformerId",
                ValueColumn = "Url",
                EntityKind = "performer",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "PerformerUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "PerformerId" integer NOT NULL REFERENCES "performers"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_PerformerUrl_PerformerId\" ON \"PerformerUrl\" (\"PerformerId\")" }
            },
            new
            {
                Table = "StudioUrl",
                ParentTable = "studios",
                ForeignKeyColumn = "StudioId",
                ValueColumn = "Url",
                EntityKind = "studio",
                Scheme = "url",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "StudioUrl" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "StudioId" integer NOT NULL REFERENCES "studios"("Id") ON DELETE CASCADE,
                        "Url" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_StudioUrl_StudioId\" ON \"StudioUrl\" (\"StudioId\")" }
            },
            new
            {
                Table = "TagAlias",
                ParentTable = "tags",
                ForeignKeyColumn = "TagId",
                ValueColumn = "Alias",
                EntityKind = "tag",
                Scheme = "alias",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "TagAlias" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "TagId" integer NOT NULL REFERENCES "tags"("Id") ON DELETE CASCADE,
                        "Alias" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_TagAlias_TagId\" ON \"TagAlias\" (\"TagId\")" }
            },
            new
            {
                Table = "StudioAlias",
                ParentTable = "studios",
                ForeignKeyColumn = "StudioId",
                ValueColumn = "Alias",
                EntityKind = "studio",
                Scheme = "alias",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "StudioAlias" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "StudioId" integer NOT NULL REFERENCES "studios"("Id") ON DELETE CASCADE,
                        "Alias" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_StudioAlias_StudioId\" ON \"StudioAlias\" (\"StudioId\")" }
            },
            new
            {
                Table = "PerformerAlias",
                ParentTable = "performers",
                ForeignKeyColumn = "PerformerId",
                ValueColumn = "Alias",
                EntityKind = "performer",
                Scheme = "alias",
                IncludeEndpoint = false,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "PerformerAlias" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "PerformerId" integer NOT NULL REFERENCES "performers"("Id") ON DELETE CASCADE,
                        "Alias" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_PerformerAlias_PerformerId\" ON \"PerformerAlias\" (\"PerformerId\")" }
            },
            new
            {
                Table = "SceneRemoteId",
                ParentTable = "scenes",
                ForeignKeyColumn = "SceneId",
                ValueColumn = "RemoteId",
                EntityKind = "scene",
                Scheme = "remote_id",
                IncludeEndpoint = true,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "SceneRemoteId" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "SceneId" integer NOT NULL REFERENCES "scenes"("Id") ON DELETE CASCADE,
                        "Endpoint" text NOT NULL,
                        "RemoteId" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_SceneRemoteId_SceneId\" ON \"SceneRemoteId\" (\"SceneId\")" }
            },
            new
            {
                Table = "PerformerRemoteId",
                ParentTable = "performers",
                ForeignKeyColumn = "PerformerId",
                ValueColumn = "RemoteId",
                EntityKind = "performer",
                Scheme = "remote_id",
                IncludeEndpoint = true,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "PerformerRemoteId" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "PerformerId" integer NOT NULL REFERENCES "performers"("Id") ON DELETE CASCADE,
                        "Endpoint" text NOT NULL,
                        "RemoteId" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_PerformerRemoteId_PerformerId\" ON \"PerformerRemoteId\" (\"PerformerId\")" }
            },
            new
            {
                Table = "TagRemoteId",
                ParentTable = "tags",
                ForeignKeyColumn = "TagId",
                ValueColumn = "RemoteId",
                EntityKind = "tag",
                Scheme = "remote_id",
                IncludeEndpoint = true,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "TagRemoteId" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "TagId" integer NOT NULL REFERENCES "tags"("Id") ON DELETE CASCADE,
                        "Endpoint" text NOT NULL,
                        "RemoteId" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_TagRemoteId_TagId\" ON \"TagRemoteId\" (\"TagId\")" }
            },
            new
            {
                Table = "StudioRemoteId",
                ParentTable = "studios",
                ForeignKeyColumn = "StudioId",
                ValueColumn = "RemoteId",
                EntityKind = "studio",
                Scheme = "remote_id",
                IncludeEndpoint = true,
                CreateSql = """
                    CREATE TABLE IF NOT EXISTS "StudioRemoteId" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "StudioId" integer NOT NULL REFERENCES "studios"("Id") ON DELETE CASCADE,
                        "Endpoint" text NOT NULL,
                        "RemoteId" text NOT NULL
                    )
                """,
                IndexSql = new[] { "CREATE INDEX IF NOT EXISTS \"IX_StudioRemoteId_StudioId\" ON \"StudioRemoteId\" (\"StudioId\")" }
            },
        };

        foreach (var relation in legacyIdentifierRelations)
        {
            await EnsureLegacyIdentifierTableAsync(
                relation.Table,
                relation.ParentTable,
                relation.ForeignKeyColumn,
                relation.ValueColumn,
                relation.EntityKind,
                relation.Scheme,
                relation.CreateSql,
                relation.IndexSql,
                relation.IncludeEndpoint);
        }

        var remoteIdIndexCommands = new[]
        {
            new { Relation = "SceneRemoteId", Sql = "CREATE INDEX IF NOT EXISTS \"IX_SceneRemoteId_SceneId\" ON \"SceneRemoteId\" (\"SceneId\")" },
            new { Relation = "PerformerRemoteId", Sql = "CREATE INDEX IF NOT EXISTS \"IX_PerformerRemoteId_PerformerId\" ON \"PerformerRemoteId\" (\"PerformerId\")" },
            new { Relation = "TagRemoteId", Sql = "CREATE INDEX IF NOT EXISTS \"IX_TagRemoteId_TagId\" ON \"TagRemoteId\" (\"TagId\")" },
            new { Relation = "StudioRemoteId", Sql = "CREATE INDEX IF NOT EXISTS \"IX_StudioRemoteId_StudioId\" ON \"StudioRemoteId\" (\"StudioId\")" },
        };

        foreach (var indexCommand in remoteIdIndexCommands)
        {
            var relationKind = await GetRelationKindAsync(indexCommand.Relation);
            if (relationKind is not ("r" or "p"))
            {
                Log.Information("Skipping compatibility index bootstrap for relation {Relation} (kind={Kind})", indexCommand.Relation, relationKind ?? "missing");
                continue;
            }

            await using var createIndex = conn.CreateCommand();
            createIndex.CommandText = indexCommand.Sql;
            await createIndex.ExecuteNonQueryAsync();
        }

        await EnsureTableExists("extension_data", """
            CREATE TABLE IF NOT EXISTS "extension_data" (
                "ExtensionId" character varying(256) NOT NULL,
                "Key" character varying(512) NOT NULL,
                "Value" text NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                PRIMARY KEY ("ExtensionId", "Key")
            )
        """);

        await using (var extIndex = conn.CreateCommand())
        {
            extIndex.CommandText = "CREATE INDEX IF NOT EXISTS \"IX_extension_data_ExtensionId\" ON \"extension_data\" (\"ExtensionId\")";
            await extIndex.ExecuteNonQueryAsync();
        }

        await EnsureTableExists("custom_field_definitions", """
            CREATE TABLE IF NOT EXISTS "custom_field_definitions" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "Key" character varying(100) NOT NULL,
                "Label" character varying(200) NOT NULL,
                "Type" character varying(50) NOT NULL,
                "EntityTypes" text[] NOT NULL,
                "Options" text[] NOT NULL,
                "Filterable" boolean NOT NULL,
                "Sortable" boolean NOT NULL,
                "IsMultiValue" boolean NOT NULL,
                "DisplayOrder" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            )
        """);

        await EnsureTableExists("custom_field_values", """
            CREATE TABLE IF NOT EXISTS "custom_field_values" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "DefinitionId" integer NOT NULL REFERENCES "custom_field_definitions"("Id") ON DELETE CASCADE,
                "EntityType" character varying(50) NOT NULL,
                "EntityId" integer NOT NULL,
                "Position" integer NOT NULL,
                "TextValue" character varying(4000) NULL,
                "NumberValue" numeric(18,6) NULL,
                "BoolValue" boolean NULL,
                "DateValue" date NULL,
                "TimestampValue" timestamp with time zone NULL,
                "IntegerValue" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            )
        """);

        var customFieldIndexCommands = new[]
        {
            new { Relation = "custom_field_definitions", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_definitions_DisplayOrder\" ON \"custom_field_definitions\" (\"DisplayOrder\")" },
            new { Relation = "custom_field_definitions", Sql = "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_custom_field_definitions_Key\" ON \"custom_field_definitions\" (\"Key\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_BoolValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"BoolValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_DateValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"DateValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_EntityId_Positi~\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"EntityId\", \"Position\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_IntegerValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"IntegerValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_NumberValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"NumberValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_TextValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"TextValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_DefinitionId_EntityType_TimestampValue\" ON \"custom_field_values\" (\"DefinitionId\", \"EntityType\", \"TimestampValue\")" },
            new { Relation = "custom_field_values", Sql = "CREATE INDEX IF NOT EXISTS \"IX_custom_field_values_EntityType_EntityId\" ON \"custom_field_values\" (\"EntityType\", \"EntityId\")" },
        };

        foreach (var indexCommand in customFieldIndexCommands)
        {
            var relationKind = await GetRelationKindAsync(indexCommand.Relation);
            if (relationKind is not ("r" or "p"))
                continue;

            await using var indexCmd = conn.CreateCommand();
            indexCmd.CommandText = indexCommand.Sql;
            await indexCmd.ExecuteNonQueryAsync();
        }

        await EnsureTableExists("VideoCaptions", """
            CREATE TABLE IF NOT EXISTS "VideoCaptions" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "FileId" integer NOT NULL REFERENCES "files"("Id") ON DELETE CASCADE,
                "LanguageCode" text NOT NULL,
                "CaptionType" text NOT NULL,
                "Filename" text NOT NULL
            )
        """);

        await AddColumnIfMissing("VideoCaptions", "Id", "integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY");

        var segmentRuleRelationKind = await GetRelationKindAsync("segment_display_rules");
        if (segmentRuleRelationKind is "r" or "p")
        {
            await EnsureTableExists("segment_display_profiles", """
                CREATE TABLE IF NOT EXISTS "segment_display_profiles" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "Name" character varying(200) NOT NULL,
                    "Description" character varying(1000) NULL,
                    "UserId" integer NULL,
                    "IsSystem" boolean NOT NULL,
                    "IsDefault" boolean NOT NULL,
                    "Version" integer NOT NULL DEFAULT 1,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                )
            """);

            await AddColumnIfMissing("segment_display_rules", "ProfileId", "integer", skipIfTableMissing: true);

            await EnsureTableExists("group_items", """
                CREATE TABLE IF NOT EXISTS "group_items" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "GroupId" integer NOT NULL REFERENCES "groups"("Id") ON DELETE CASCADE,
                    "OrderIndex" integer NOT NULL,
                    "Kind" integer NOT NULL,
                    "SceneId" integer NOT NULL REFERENCES "scenes"("Id") ON DELETE CASCADE,
                    "StartSec" double precision NULL,
                    "EndSec" double precision NULL,
                    "Title" text NULL,
                    "Notes" text NULL,
                    "SourceSpanKey" text NULL,
                    "SourceProfileId" integer NULL,
                    "SourceQueryJson" text NULL,
                    "SnapshotAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                )
            """);

            await AddColumnIfMissing("group_items", "SourceQueryJson", "text");

            await ExecuteNonQueryAsync("""
                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                SELECT 'Raw', 'Built-in raw segment display profile', NULL, TRUE, FALSE, 1, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "segment_display_profiles"
                    WHERE "UserId" IS NULL AND "Name" = 'Raw'
                );

                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                SELECT 'Default', 'Built-in default segment display profile', NULL, TRUE, TRUE, 1, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "segment_display_profiles"
                    WHERE "UserId" IS NULL AND "IsDefault" = TRUE
                );

                INSERT INTO "segment_display_profiles" ("Name", "Description", "UserId", "IsSystem", "IsDefault", "Version", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT 'Default', 'User default segment display profile', rules."UserId", FALSE, TRUE, 1, now(), now()
                FROM "segment_display_rules" rules
                WHERE rules."UserId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "segment_display_profiles" profiles
                      WHERE profiles."UserId" = rules."UserId" AND profiles."IsDefault" = TRUE
                  );

                UPDATE "segment_display_rules" AS rules
                SET "ProfileId" = profiles."Id"
                FROM "segment_display_profiles" AS profiles
                WHERE rules."ProfileId" IS NULL
                  AND (
                      (rules."UserId" IS NULL AND profiles."UserId" IS NULL AND profiles."IsDefault" = TRUE)
                      OR (rules."UserId" IS NOT NULL AND profiles."UserId" = rules."UserId" AND profiles."IsDefault" = TRUE)
                  );

                INSERT INTO "segment_display_rules" ("ProfileId", "SourceKey", "Visible", "MinDurationSec", "MergeGapSec", "CollapseToInstant", "CreatedAt", "UpdatedAt")
                SELECT profiles."Id", 'ext:ai.%', TRUE, 1.5, 2.0, FALSE, now(), now()
                FROM "segment_display_profiles" profiles
                WHERE profiles."UserId" IS NULL AND profiles."IsDefault" = TRUE
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "segment_display_rules" rules
                      WHERE rules."ProfileId" = profiles."Id" AND rules."SourceKey" = 'ext:ai.%'
                  );
            """);
        }

        var segmentCompatibilityIndexCommands = new[]
        {
            new { Relation = "segment_display_profiles", Sql = "CREATE INDEX IF NOT EXISTS \"IX_segment_display_profiles_UserId\" ON \"segment_display_profiles\" (\"UserId\")" },
            new { Relation = "segment_display_profiles", Sql = "CREATE INDEX IF NOT EXISTS \"IX_segment_display_profiles_UserId_IsDefault\" ON \"segment_display_profiles\" (\"UserId\", \"IsDefault\")" },
            new { Relation = "segment_display_profiles", Sql = "CREATE INDEX IF NOT EXISTS \"IX_segment_display_profiles_IsSystem_Name\" ON \"segment_display_profiles\" (\"IsSystem\", \"Name\")" },
            new { Relation = "segment_display_rules", Sql = "CREATE INDEX IF NOT EXISTS \"IX_segment_display_rules_ProfileId\" ON \"segment_display_rules\" (\"ProfileId\")" },
            new { Relation = "segment_display_rules", Sql = "CREATE INDEX IF NOT EXISTS \"IX_segment_display_rules_ProfileId_SourceKey_Kind_TagId_TagCategory_HostType_Priority\" ON \"segment_display_rules\" (\"ProfileId\", \"SourceKey\", \"Kind\", \"TagId\", \"TagCategory\", \"HostType\", \"Priority\")" },
            new { Relation = "group_items", Sql = "CREATE INDEX IF NOT EXISTS \"IX_group_items_GroupId_OrderIndex\" ON \"group_items\" (\"GroupId\", \"OrderIndex\")" },
            new { Relation = "group_items", Sql = "CREATE INDEX IF NOT EXISTS \"IX_group_items_SceneId\" ON \"group_items\" (\"SceneId\")" },
            new { Relation = "group_items", Sql = "CREATE INDEX IF NOT EXISTS \"IX_group_items_SourceProfileId\" ON \"group_items\" (\"SourceProfileId\")" },
        };
        foreach (var indexCommand in segmentCompatibilityIndexCommands)
        {
            var relationKind = await GetRelationKindAsync(indexCommand.Relation);
            if (relationKind is not ("r" or "p"))
                continue;

            await using var indexCmd = conn.CreateCommand();
            indexCmd.CommandText = indexCommand.Sql;
            await indexCmd.ExecuteNonQueryAsync();
        }

        await conn.CloseAsync();
    }

    public static async Task NormalizeOshashAndIndexesAsync(CoveContext db)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return;

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE "FileFingerprints"
            SET "Value" = substr('0000000000000000' || "Value", -16, 16)
            WHERE "Type" = 'oshash' AND length("Value") < 16
        """;
        var affected = await cmd.ExecuteNonQueryAsync();

        var indexCommands = new[]
        {
            "CREATE INDEX IF NOT EXISTS \"IX_scene_tags_TagId\" ON \"scene_tags\" (\"TagId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_scene_performers_PerformerId\" ON \"scene_performers\" (\"PerformerId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_image_tags_TagId\" ON \"image_tags\" (\"TagId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_image_performers_PerformerId\" ON \"image_performers\" (\"PerformerId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_image_galleries_GalleryId\" ON \"image_galleries\" (\"GalleryId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_gallery_tags_TagId\" ON \"gallery_tags\" (\"TagId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_gallery_performers_PerformerId\" ON \"gallery_performers\" (\"PerformerId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_performer_tags_TagId\" ON \"performer_tags\" (\"TagId\")",
            "CREATE INDEX IF NOT EXISTS \"IX_FileFingerprints_Type_Value\" ON \"FileFingerprints\" (\"Type\", \"Value\")",
        };

        foreach (var sql in indexCommands)
        {
            try
            {
                await using var idxCmd = conn.CreateCommand();
                idxCmd.CommandText = sql;
                await idxCmd.ExecuteNonQueryAsync();
            }
            catch
            {
            }
        }

        await conn.CloseAsync();

        if (affected > 0)
            Log.Information("Normalized {Count} oshash fingerprint values to 16-char padded format", affected);
    }
}