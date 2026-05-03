using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private static string OpenReadOnly(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();

    private static async Task<DbConnection> OpenAiImportConnectionAsync(string aiDataSource, CancellationToken ct)
    {
        var trimmed = aiDataSource.Trim();
        if (IsSqliteAiDataSource(trimmed))
        {
            var sqlite = new SqliteConnection(BuildSqliteAiConnectionString(trimmed));
            await sqlite.OpenAsync(ct);
            return sqlite;
        }

        var npgsql = new NpgsqlConnection(BuildNpgsqlAiConnectionString(trimmed));
        await npgsql.OpenAsync(ct);
        return npgsql;
    }

    private static async Task<int> CountAsync(SqliteConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM \"{table}\"";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name = @column";
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<int> CountAiRowsAsync(DbConnection conn, string table, string? whereClause, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.IsNullOrWhiteSpace(whereClause)
            ? $"SELECT count(*) FROM \"{table}\""
            : $"SELECT count(*) FROM \"{table}\" WHERE {whereClause}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<bool> AiTableExistsAsync(DbConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (conn is SqliteConnection)
        {
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name";
        }
        else if (conn is NpgsqlConnection)
        {
            cmd.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @name";
        }
        else
        {
            throw new NotSupportedException($"Unsupported AI import provider: {conn.GetType().Name}");
        }

        AddDbParameter(cmd, "@name", table);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<bool> AiColumnExistsAsync(DbConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (conn is SqliteConnection)
        {
            cmd.CommandText = $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name = @column";
        }
        else if (conn is NpgsqlConnection)
        {
            cmd.CommandText = "SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = @column";
            AddDbParameter(cmd, "@table", table);
        }
        else
        {
            throw new NotSupportedException($"Unsupported AI import provider: {conn.GetType().Name}");
        }

        AddDbParameter(cmd, "@column", column);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<Dictionary<int, List<string>>> ReadUrlsAsync(SqliteConnection conn, string table, string fkCol, CancellationToken ct)
    {
        var result = new Dictionary<int, List<string>>();
        if (!await TableExistsAsync(conn, table, ct)) return result;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"{fkCol}\", url FROM \"{table}\" ORDER BY \"{fkCol}\", position";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            if (!result.TryGetValue(id, out var list)) result[id] = list = [];
            list.Add(r.GetString(1));
        }
        return result;
    }

    private static async Task<Dictionary<int, List<string>>> ReadAliasesAsync(SqliteConnection conn, string table, string fkCol, CancellationToken ct)
    {
        var result = new Dictionary<int, List<string>>();
        if (!await TableExistsAsync(conn, table, ct)) return result;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"{fkCol}\", alias FROM \"{table}\"";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            if (!result.TryGetValue(id, out var list)) result[id] = list = [];
            list.Add(r.GetString(1));
        }
        return result;
    }

    private static async Task<Dictionary<int, List<int>>> ReadJunctionAsync(SqliteConnection conn, string table, string fkA, string fkB, CancellationToken ct)
    {
        var result = new Dictionary<int, List<int>>();
        if (!await TableExistsAsync(conn, table, ct)) return result;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"{fkA}\", \"{fkB}\" FROM \"{table}\"";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var a = r.GetInt32(0);
            var b = r.GetInt32(1);
            if (!result.TryGetValue(a, out var list)) result[a] = list = [];
            list.Add(b);
        }
        return result;
    }

    private static async Task<IReadOnlyDictionary<string, int>> BuildStashTagNameToCoveIdMapAsync(
        SqliteConnection conn,
        IReadOnlyDictionary<int, int> tagIdMap,
        CancellationToken ct)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!await TableExistsAsync(conn, "tags", ct))
            return result;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM tags";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var stashTagId = r.GetInt32(0);
            var name = ReadStringNull(r, 1);
            if (!tagIdMap.TryGetValue(stashTagId, out var coveTagId) || string.IsNullOrWhiteSpace(name))
                continue;
            result[name] = coveTagId;
        }

        return result;
    }

    private static async Task<Dictionary<int, List<(int? ModelId, string? Name, double? Version, string? ModelType, string? CategoriesJson, string? ExtraJson, string? InputParamsJson, double? FrameInterval)>>> ReadAiRunModelsAsync(DbConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<int, List<(int? ModelId, string? Name, double? Version, string? ModelType, string? CategoriesJson, string? ExtraJson, string? InputParamsJson, double? FrameInterval)>>();
        if (!await AiTableExistsAsync(conn, "ai_model_run_models", ct))
            return result;

        var hasAiModels = await AiTableExistsAsync(conn, "ai_models", ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = hasAiModels
            ? @"SELECT rm.run_id, m.model_id, m.name, m.version, m.model_type, m.categories, m.extra, rm.input_params, rm.frame_interval
                FROM ai_model_run_models rm
                LEFT JOIN ai_models m ON m.id = rm.model_id
                ORDER BY rm.run_id, rm.id"
            : @"SELECT rm.run_id, NULL, NULL, NULL, NULL, NULL, NULL, rm.input_params, rm.frame_interval
                FROM ai_model_run_models rm
                ORDER BY rm.run_id, rm.id";

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var runId = ReadDbIntNull(r, 0);
            if (!runId.HasValue)
                continue;

            if (!result.TryGetValue(runId.Value, out var list))
                result[runId.Value] = list = [];

            list.Add((
                ReadDbIntNull(r, 1),
                ReadDbStringNull(r, 2),
                ReadDbDoubleNull(r, 3),
                ReadDbStringNull(r, 4),
                ReadDbStringNull(r, 5),
                ReadDbStringNull(r, 6),
                ReadDbStringNull(r, 7),
                ReadDbDoubleNull(r, 8)));
        }

        return result;
    }

    private static async Task<Dictionary<int, List<DateTime>>> ReadDatesAsync(SqliteConnection conn, string table, string fkCol, string dateCol, CancellationToken ct)
    {
        var result = new Dictionary<int, List<DateTime>>();
        if (!await TableExistsAsync(conn, table, ct)) return result;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"{fkCol}\", \"{dateCol}\" FROM \"{table}\"";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            var s = ReadStringNull(r, 1);
            if (s == null) continue;
            if (!result.TryGetValue(id, out var list)) result[id] = list = [];
            list.Add(ParseDateTime(s));
        }
        return result;
    }

    private static List<int> TopologicalSort(List<int> ids, Func<int, IEnumerable<int>> getDeps)
    {
        var result = new List<int>(ids.Count);
        var visited = new HashSet<int>(ids.Count);
        var inProgress = new HashSet<int>();
        var idSet = new HashSet<int>(ids);

        void Visit(int id)
        {
            if (visited.Contains(id) || inProgress.Contains(id)) return;
            inProgress.Add(id);
            foreach (var dep in getDeps(id))
                if (idSet.Contains(dep)) Visit(dep);
            inProgress.Remove(id);
            visited.Add(id);
            result.Add(id);
        }

        foreach (var id in ids) Visit(id);
        return result;
    }

    private static string? ReadStringNull(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? ReadIntNull(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static bool ReadBool(SqliteDataReader r, int i) => !r.IsDBNull(i) && r.GetBoolean(i);

    private static string? ReadDbStringNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
    private static int? ReadDbIntNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    private static long? ReadDbLongNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);
    private static double? ReadDbDoubleNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i), CultureInfo.InvariantCulture);
    private static DateTime? ReadDbDateTimeNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : NormalizeImportedDateTime(r.GetValue(i));

    private static DateOnly? ParseDate(string? s) =>
        s != null && DateOnly.TryParse(s, out var d) ? d : null;

    private static DateTime ParseDateTime(string s) =>
        DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.UtcNow;

    private static DateTime? ParseDateTimeOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : ParseDateTime(s);

    private static DateTime NormalizeImportedDateTime(object raw) =>
        raw switch
        {
            DateTime value => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeOffset value => value.UtcDateTime,
            string value when !string.IsNullOrWhiteSpace(value) => ParseDateTime(value),
            _ => DateTime.SpecifyKind(Convert.ToDateTime(raw, CultureInfo.InvariantCulture), DateTimeKind.Utc),
        };

    private static void AddDbParameter(DbCommand cmd, string name, object? value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(parameter);
    }

    private static bool IsSqliteAiDataSource(string dataSource)
    {
        if (dataSource.StartsWith("sqlite://", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (File.Exists(dataSource))
            return true;

        var extension = Path.GetExtension(dataSource);
        return string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sqlite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSqliteAiConnectionString(string dataSource)
    {
        if (dataSource.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            var existing = new SqliteConnectionStringBuilder(dataSource)
            {
                Mode = SqliteOpenMode.ReadOnly,
            };
            return existing.ToString();
        }

        var path = dataSource;
        if (dataSource.StartsWith("sqlite://", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(dataSource, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid SQLite AI data source.", nameof(dataSource));
            path = uri.IsFile ? uri.LocalPath : dataSource;
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"AI data source file not found: {path}", path);

        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
    }

    private static string BuildNpgsqlAiConnectionString(string dataSource)
    {
        if (dataSource.Contains("=", StringComparison.Ordinal) && !dataSource.Contains("://", StringComparison.Ordinal))
            return dataSource;

        var normalized = Regex.Replace(dataSource, "^postgresql\\+[^:]+://", "postgresql://", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, "^postgres://", "postgresql://", RegexOptions.IgnoreCase);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid PostgreSQL AI data source.", nameof(dataSource));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            if (parts.Length > 0)
                builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                builder.Password = Uri.UnescapeDataString(parts[1]);
        }

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(pieces[0]);
                var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
                try
                {
                    builder[key] = value;
                }
                catch (ArgumentException)
                {
                }
            }
        }

        return builder.ConnectionString;
    }

    private static bool TryMapAiRunTarget(
        string entityType,
        int legacyEntityId,
        IReadOnlyDictionary<int, int> sceneIdMap,
        IReadOnlyDictionary<int, int> imageIdMap,
        out AiRunTargetType targetType,
        out int targetId)
    {
        targetType = default;
        targetId = default;

        if (string.Equals(entityType, "scene", StringComparison.OrdinalIgnoreCase)
            && sceneIdMap.TryGetValue(legacyEntityId, out var sceneId))
        {
            targetType = AiRunTargetType.Scene;
            targetId = sceneId;
            return true;
        }

        if (string.Equals(entityType, "image", StringComparison.OrdinalIgnoreCase)
            && imageIdMap.TryGetValue(legacyEntityId, out var imageId))
        {
            targetType = AiRunTargetType.Image;
            targetId = imageId;
            return true;
        }

        return false;
    }

    private static bool TryMapAiSegmentHost(
        string entityType,
        int legacyEntityId,
        IReadOnlyDictionary<int, int> sceneIdMap,
        IReadOnlyDictionary<int, int> imageIdMap,
        out SegmentHostType hostType,
        out int hostId)
    {
        hostType = default;
        hostId = default;

        if (string.Equals(entityType, "scene", StringComparison.OrdinalIgnoreCase)
            && sceneIdMap.TryGetValue(legacyEntityId, out var sceneId))
        {
            hostType = SegmentHostType.Scene;
            hostId = sceneId;
            return true;
        }

        if (string.Equals(entityType, "image", StringComparison.OrdinalIgnoreCase)
            && imageIdMap.TryGetValue(legacyEntityId, out var imageId))
        {
            hostType = SegmentHostType.Image;
            hostId = imageId;
            return true;
        }

        return false;
    }

    private static bool TryResolveImportedTagId(
        int? legacyTagId,
        string? strValue,
        IReadOnlyDictionary<int, int> tagIdMap,
        IReadOnlyDictionary<string, int> tagNameToCoveIdMap,
        out int coveTagId)
    {
        if (legacyTagId.HasValue && tagIdMap.TryGetValue(legacyTagId.Value, out coveTagId))
            return true;

        if (!string.IsNullOrWhiteSpace(strValue) && tagNameToCoveIdMap.TryGetValue(strValue, out coveTagId))
            return true;

        coveTagId = default;
        return false;
    }

    private static AiRunStatus MapAiRunStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "pending" => AiRunStatus.Pending,
        "running" => AiRunStatus.Running,
        "failed" => AiRunStatus.Failed,
        "cancelled" => AiRunStatus.Cancelled,
        _ => AiRunStatus.Completed,
    };

    private static string BuildImportedAiRunKey(int legacyRunId) => $"import:stash-ai-server:{legacyRunId}";

    private static string? FirstNonEmpty(string? primary, string? secondary) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : (!string.IsNullOrWhiteSpace(secondary) ? secondary : null);

    private static double? ResolveAiFrameInterval(
        string? inputParamsJson,
        string? resultMetadataJson,
        IReadOnlyList<(int? ModelId, string? Name, double? Version, string? ModelType, string? CategoriesJson, string? ExtraJson, string? InputParamsJson, double? FrameInterval)> models)
    {
        foreach (var model in models)
        {
            if (model.FrameInterval.HasValue)
                return model.FrameInterval.Value;
        }

        return ExtractJsonDouble(inputParamsJson, "frame_interval")
            ?? ExtractJsonDouble(resultMetadataJson, "frame_interval");
    }

    private static JsonDocument? ParseJsonDocumentOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? ParseJsonNodeOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonDocument? BuildImportedAiModelsDocument(
        IReadOnlyList<(int? ModelId, string? Name, double? Version, string? ModelType, string? CategoriesJson, string? ExtraJson, string? InputParamsJson, double? FrameInterval)> models)
    {
        if (models.Count == 0)
            return null;

        var items = new JsonArray();
        foreach (var model in models)
        {
            var item = new JsonObject();
            if (model.ModelId.HasValue) item["modelId"] = model.ModelId.Value;
            if (!string.IsNullOrWhiteSpace(model.Name)) item["name"] = model.Name;
            if (model.Version.HasValue) item["version"] = model.Version.Value;
            if (!string.IsNullOrWhiteSpace(model.ModelType)) item["modelType"] = model.ModelType;
            if (ParseJsonNodeOrNull(model.CategoriesJson) is JsonNode categoriesNode) item["categories"] = categoriesNode;
            if (ParseJsonNodeOrNull(model.ExtraJson) is JsonNode extraNode) item["extra"] = extraNode;
            if (ParseJsonNodeOrNull(model.InputParamsJson) is JsonNode inputNode) item["inputParams"] = inputNode;
            if (model.FrameInterval.HasValue) item["frameInterval"] = model.FrameInterval.Value;
            items.Add(item);
        }

        return JsonSerializer.SerializeToDocument(items);
    }

    private static JsonDocument BuildImportedAiSummaryDocument(int legacyRunId, string? service, string? pluginName, string? resultMetadataJson)
    {
        var summary = new JsonObject
        {
            ["legacyRunId"] = legacyRunId,
        };
        if (!string.IsNullOrWhiteSpace(service)) summary["service"] = service;
        if (!string.IsNullOrWhiteSpace(pluginName)) summary["pluginName"] = pluginName;
        if (ParseJsonNodeOrNull(resultMetadataJson) is JsonNode metadataNode) summary["resultMetadata"] = metadataNode;
        return JsonSerializer.SerializeToDocument(summary);
    }

    private static JsonDocument? BuildImportedAiSegmentPayload(string? category, string? strValue, string? valueJson)
    {
        var payload = new JsonObject();
        if (!string.IsNullOrWhiteSpace(category)) payload["category"] = category;
        if (!string.IsNullOrWhiteSpace(strValue)) payload["strValue"] = strValue;
        if (ParseJsonNodeOrNull(valueJson) is JsonNode valueNode) payload["valueJson"] = valueNode;
        return payload.Count == 0 ? null : JsonSerializer.SerializeToDocument(payload);
    }

    private static string? ExtractJsonString(string? json, string propertyName)
    {
        if (ParseJsonDocumentOrNull(json) is not JsonDocument doc)
            return null;
        if (!doc.RootElement.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static bool? ExtractJsonBool(string? json, string propertyName)
    {
        if (ParseJsonDocumentOrNull(json) is not JsonDocument doc)
            return null;
        if (!doc.RootElement.TryGetProperty(propertyName, out var property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static double? ExtractJsonDouble(string? json, string propertyName)
    {
        if (ParseJsonDocumentOrNull(json) is not JsonDocument doc)
            return null;
        if (!doc.RootElement.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var parsed) => parsed,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static float? ExtractJsonFloat(string? json, string propertyName)
    {
        var value = ExtractJsonDouble(json, propertyName);
        return value.HasValue ? (float)value.Value : null;
    }

    private static string? ResolveImportedGalleryTitle(
        string? explicitTitle,
        int? folderId,
        int stashGalleryId,
        IReadOnlyDictionary<int, int> galleryToFile,
        IReadOnlyDictionary<int, (string Basename, int FolderId, long Size, DateTime ModTime, DateTime CreatedAt)> fileData,
        IReadOnlyDictionary<int, string> stashFolderNames)
    {
        if (!string.IsNullOrWhiteSpace(explicitTitle))
            return explicitTitle;

        if (galleryToFile.TryGetValue(stashGalleryId, out var fileId) && fileData.TryGetValue(fileId, out var file))
            return Path.GetFileNameWithoutExtension(file.Basename);

        if (folderId.HasValue && stashFolderNames.TryGetValue(folderId.Value, out var folderName) && !string.IsNullOrWhiteSpace(folderName))
            return folderName;

        return null;
    }

    private static string? GetBlobId(Dictionary<string, string> blobMap, string? checksum) =>
        checksum != null && blobMap.TryGetValue(checksum, out var id) ? id : null;

    private static string? GetFingerprintValue(List<(string Type, string Value)> fingerprints, string type) =>
        fingerprints.FirstOrDefault(fp => string.Equals(fp.Type, type, StringComparison.OrdinalIgnoreCase)).Value;

    private static string NormalizeImportedFingerprintValue(string type, object? rawValue)
    {
        var value = rawValue switch
        {
            byte[] fpBytes => Encoding.UTF8.GetString(fpBytes),
            long number when string.Equals(type, "phash", StringComparison.OrdinalIgnoreCase) => unchecked((ulong)number).ToString("x"),
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => rawValue?.ToString() ?? string.Empty,
        };

        return value.Trim();
    }

    private static string GetLastPathSegment(string path)
    {
        var normalizedPath = path.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return path;

        var separatorIndex = normalizedPath.LastIndexOf('/');
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    private static string NormalizeImportedPath(string path)
        => path.Replace('\\', '/');

    private static IReadOnlyList<string> GetImportedPathLookupCandidates(string path)
    {
        var normalizedPath = NormalizeImportedPath(path);
        return string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase)
            ? [normalizedPath]
            : [path, normalizedPath];
    }

    private static string? TrimGeneratedSuffix(string? value, string suffix)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
    }

    private static async Task<HashSet<int>> GetLegacyAiMarkerTagIdsAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "tags", ct))
            return [];

        var aiRootIds = new HashSet<int>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM tags";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (string.Equals(r.GetString(1), "AI", StringComparison.OrdinalIgnoreCase))
                    aiRootIds.Add(r.GetInt32(0));
            }
        }

        if (aiRootIds.Count == 0 || !await TableExistsAsync(conn, "tags_relations", ct))
            return aiRootIds;

        var childMap = new Dictionary<int, List<int>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT parent_id, child_id FROM tags_relations";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var parentId = r.GetInt32(0);
                var childId = r.GetInt32(1);
                if (!childMap.TryGetValue(parentId, out var children))
                    childMap[parentId] = children = [];
                children.Add(childId);
            }
        }

        var allAiTagIds = new HashSet<int>(aiRootIds);
        var queue = new Queue<int>(aiRootIds);
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var childId in childMap.GetValueOrDefault(currentId, []))
            {
                if (allAiTagIds.Add(childId))
                    queue.Enqueue(childId);
            }
        }

        return allAiTagIds;
    }

    private static void ReportPhase(IJobProgress progress, double startProgress, double endProgress, int completed, int total, string subTask)
    {
        var ratio = total <= 0 ? 1 : Math.Clamp((double)completed / total, 0, 1);
        progress.Report(startProgress + ((endProgress - startProgress) * ratio), subTask);
    }

    private static void TrimImportResultsLocked()
    {
        while (importResultOrder.Count > 20)
        {
            var oldestJobId = importResultOrder.Dequeue();
            importResults.Remove(oldestJobId);
        }
    }

    private static void TrimAiImportResultsLocked()
    {
        while (aiImportResultOrder.Count > 20)
        {
            var oldestJobId = aiImportResultOrder.Dequeue();
            aiImportResults.Remove(oldestJobId);
        }
    }

    private async Task<Dictionary<int, int>> BuildExistingSceneIdMapAsync(CancellationToken ct)
    {
        var remoteIds = await _db.Set<SceneRemoteId>()
            .AsNoTracking()
            .Select(remote => new { remote.SceneId, remote.Endpoint, remote.RemoteId })
            .ToListAsync(ct);

        var result = new Dictionary<int, int>();
        foreach (var row in remoteIds.OrderByDescending(remote => remote.Endpoint.Contains("stash", StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse(row.RemoteId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stashSceneId))
                continue;
            result.TryAdd(stashSceneId, row.SceneId);
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, int>> BuildCoveTagNameMapAsync(CancellationToken ct)
    {
        var tags = await _db.Tags
            .AsNoTracking()
            .Select(tag => new { tag.Id, tag.Name })
            .ToListAsync(ct);

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
                continue;
            result[tag.Name] = tag.Id;
        }

        return result;
    }

    private static async Task<Dictionary<int, int>> BuildExistingTagIdMapAsync(
        SqliteConnection conn,
        IReadOnlyDictionary<string, int> coveTagNameMap,
        CancellationToken ct)
    {
        var result = new Dictionary<int, int>();
        if (!await TableExistsAsync(conn, "tags", ct))
            return result;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM tags";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var stashTagId = r.GetInt32(0);
            var tagName = ReadStringNull(r, 1);
            if (string.IsNullOrWhiteSpace(tagName))
                continue;
            if (coveTagNameMap.TryGetValue(tagName, out var coveTagId))
                result[stashTagId] = coveTagId;
        }

        return result;
    }

    private sealed class NullJobProgress : IJobProgress
    {
        public static readonly NullJobProgress Instance = new();

        public void Report(double progress, string? subTask = null)
        {
        }
    }

    private static GenderEnum? ParseGender(string? s) => s?.ToUpperInvariant() switch
    {
        "MALE" => GenderEnum.Male,
        "FEMALE" => GenderEnum.Female,
        "TRANSGENDER_MALE" => GenderEnum.TransgenderMale,
        "TRANSGENDER_FEMALE" => GenderEnum.TransgenderFemale,
        "INTERSEX" => GenderEnum.Intersex,
        "NON_BINARY" => GenderEnum.NonBinary,
        _ => null,
    };

    private static CircumcisedEnum? ParseCircumcised(string? s) => s?.ToUpperInvariant() switch
    {
        "CUT" => CircumcisedEnum.Cut,
        "UNCUT" => CircumcisedEnum.Uncut,
        _ => null,
    };

    private static (DateOnly? start, DateOnly? end) ParseCareerLength(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        var parts = s.Split('-', StringSplitOptions.TrimEntries);
        DateOnly? start = null;
        DateOnly? end = null;
        if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var sy) && sy > 1900 && sy < 2100)
            start = new DateOnly(sy, 1, 1);
        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]) && int.TryParse(parts[1].Trim(), out var ey) && ey > 1900 && ey < 2100)
            end = new DateOnly(ey, 12, 31);
        return (start, end);
    }
}