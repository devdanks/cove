using System.Text.Json;
using Microsoft.Data.Sqlite;
using Cove.Core.Entities;
using Cove.Core.Enums;
using Cove.Core.Interfaces;

namespace Cove.Api.Services;

public partial class StashMigrationService
{
    private async Task<(int count, Dictionary<int, int> sceneIdMap, Dictionary<int, SceneGeneratedData> generatedMap)> ImportScenesAsync(
        SqliteConnection conn,
        Dictionary<string, string> blobMap,
        Dictionary<int, int> folderIdMap,
        Dictionary<int, int> studioIdMap,
        Dictionary<int, int> tagIdMap,
        Dictionary<int, int> performerIdMap,
        Dictionary<int, int> groupIdMap,
        IJobProgress progress,
        double startProgress,
        double endProgress,
        CancellationToken ct)
    {
        var sceneRows = new List<(int Id, string? Title, string? Details, string? Date, int? Rating,
            int? StudioId, bool Organized, string? Code, string? Director,
            double ResumeTime, double PlayDuration, string CreatedAt, string UpdatedAt, string? CoverBlob, string? LastPlayedAt)>();
        var hasSceneCoverBlob = await ColumnExistsAsync(conn, "scenes", "cover_blob", ct);
        var hasSceneLastPlayedAt = await ColumnExistsAsync(conn, "scenes", "last_played_at", ct);
        await using (var cmd = conn.CreateCommand())
        {
            var coverBlobExpr = hasSceneCoverBlob ? "cover_blob" : "NULL";
            var lastPlayedAtExpr = hasSceneLastPlayedAt ? "last_played_at" : "NULL";
            cmd.CommandText = $@"SELECT id, title, details, date, rating, studio_id, organized, code, director,
                resume_time, play_duration, created_at, updated_at, {coverBlobExpr} AS cover_blob, {lastPlayedAtExpr} AS last_played_at FROM scenes";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                sceneRows.Add((r.GetInt32(0), ReadStringNull(r, 1), ReadStringNull(r, 2), ReadStringNull(r, 3),
                    ReadIntNull(r, 4), ReadIntNull(r, 5), ReadBool(r, 6), ReadStringNull(r, 7),
                    ReadStringNull(r, 8), r.GetDouble(9), r.GetDouble(10), r.GetString(11), r.GetString(12),
                    ReadStringNull(r, 13), ReadStringNull(r, 14)));
        }

        var sceneTagMap = await ReadJunctionAsync(conn, "scenes_tags", "scene_id", "tag_id", ct);
        var scenePerformerMap = await ReadJunctionAsync(conn, "performers_scenes", "scene_id", "performer_id", ct);
        var sceneGroupMap = new Dictionary<int, List<(int GroupId, int Index)>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT scene_id, group_id, scene_index FROM groups_scenes";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var sId = r.GetInt32(0);
                var gId = r.GetInt32(1);
                var idx = ReadIntNull(r, 2) ?? 0;
                if (!sceneGroupMap.TryGetValue(sId, out var list)) sceneGroupMap[sId] = list = [];
                list.Add((gId, idx));
            }
        }
        var sceneUrls = await ReadUrlsAsync(conn, "scene_urls", "scene_id", ct);
        var sceneODates = await ReadDatesAsync(conn, "scenes_o_dates", "scene_id", "o_date", ct);
        var sceneViewDates = await ReadDatesAsync(conn, "scenes_view_dates", "scene_id", "view_date", ct);

        var sceneStashIds = new Dictionary<int, List<(string Ep, string Rid)>>();
        if (await TableExistsAsync(conn, "scene_stash_ids", ct))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT scene_id, endpoint, stash_id FROM scene_stash_ids";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var sId = r.GetInt32(0);
                if (!sceneStashIds.TryGetValue(sId, out var list)) sceneStashIds[sId] = list = [];
                list.Add((r.GetString(1), r.GetString(2)));
            }
        }

        var sceneFiles = new Dictionary<int, List<int>>();
        var scenePrimaryFileMap = new Dictionary<int, int>();
        var hasScenePrimaryColumn = await ColumnExistsAsync(conn, "scenes_files", "primary", ct);
        await using (var cmd = conn.CreateCommand())
        {
            var primaryExpr = hasScenePrimaryColumn ? "[primary]" : "0";
            cmd.CommandText = $"SELECT scene_id, file_id, {primaryExpr} AS [primary] FROM scenes_files ORDER BY scene_id, [primary] DESC, file_id";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var sId = r.GetInt32(0);
                var fId = r.GetInt32(1);
                if (!sceneFiles.TryGetValue(sId, out var list)) sceneFiles[sId] = list = [];
                list.Add(fId);
                var isPrimary = !r.IsDBNull(2) && r.GetBoolean(2);
                if (isPrimary || !scenePrimaryFileMap.ContainsKey(sId))
                    scenePrimaryFileMap[sId] = fId;
            }
        }

        var fileData = new Dictionary<int, (string Basename, int FolderId, long Size, DateTime ModTime, DateTime CreatedAt)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, basename, parent_folder_id, size, mod_time, created_at FROM files";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                fileData[r.GetInt32(0)] = (r.GetString(1), r.GetInt32(2), r.GetInt64(3),
                    ParseDateTime(r.GetString(4)), ParseDateTime(r.GetString(5)));
        }

        var videoData = new Dictionary<int, (double Duration, string VideoCodec, string Format, string AudioCodec, int Width, int Height, double FrameRate, long BitRate, bool Interactive, int? InteractiveSpeed)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT file_id, duration, video_codec, format, audio_codec, width, height, frame_rate, bit_rate, interactive, interactive_speed FROM video_files";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                videoData[r.GetInt32(0)] = (r.GetDouble(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetInt32(5), r.GetInt32(6), r.GetDouble(7), r.GetInt64(8), ReadBool(r, 9), ReadIntNull(r, 10));
        }

        var fingerprints = new Dictionary<int, List<(string Type, string Value)>>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT file_id, type, fingerprint FROM files_fingerprints";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var fId = r.GetInt32(0);
                var type = r.GetString(1);
                var rawFp = r.GetValue(2);
                var value = NormalizeImportedFingerprintValue(type, rawFp);
                if (!fingerprints.TryGetValue(fId, out var list)) fingerprints[fId] = list = [];
                list.Add((type, value));
            }
        }

        var count = 0;
        var idMap = new Dictionary<int, int>();
        const int SceneBatchSize = 50;
        var pendingBatch = new List<(int StashId, Scene Entity)>(SceneBatchSize);
        progress.Report(startProgress, "Importing scenes...");

        void FlushSceneBatch()
        {
            foreach (var (stashId, entity) in pendingBatch)
                idMap[stashId] = entity.Id;
            pendingBatch.Clear();
        }

        foreach (var row in sceneRows)
        {
            var oHistory = sceneODates.GetValueOrDefault(row.Id, []);
            var viewHistory = sceneViewDates.GetValueOrDefault(row.Id, []);
            var importedLastPlayedAt = ParseDateTimeOrNull(row.LastPlayedAt);

            var scene = new Scene
            {
                Title = row.Title,
                Details = row.Details,
                Date = ParseDate(row.Date),
                Rating = row.Rating,
                StudioId = row.StudioId.HasValue && studioIdMap.TryGetValue(row.StudioId.Value, out var sId) ? sId : null,
                Organized = row.Organized,
                Code = row.Code,
                Director = row.Director,
                ResumeTime = row.ResumeTime,
                PlayDuration = row.PlayDuration,
                OCounter = oHistory.Count,
                PlayCount = viewHistory.Count,
                LastPlayedAt = importedLastPlayedAt ?? (viewHistory.Count > 0 ? viewHistory.Max() : null),
                CreatedAt = ParseDateTime(row.CreatedAt),
                UpdatedAt = ParseDateTime(row.UpdatedAt),
                Urls = sceneUrls.GetValueOrDefault(row.Id, []).Select(u => new SceneUrl { Url = u }).ToList(),
                SceneTags = sceneTagMap.GetValueOrDefault(row.Id, [])
                    .Where(tagIdMap.ContainsKey)
                    .Select(t => new SceneTag { TagId = tagIdMap[t] }).ToList(),
                ScenePerformers = scenePerformerMap.GetValueOrDefault(row.Id, [])
                    .Where(performerIdMap.ContainsKey)
                    .Select(p => new ScenePerformer { PerformerId = performerIdMap[p] }).ToList(),
                GroupItems = sceneGroupMap.GetValueOrDefault(row.Id, [])
                    .Where(g => groupIdMap.ContainsKey(g.GroupId))
                    .Select(g => new GroupItem
                    {
                        GroupId = groupIdMap[g.GroupId],
                        OrderIndex = g.Index,
                        Kind = GroupItemKind.Scene,
                    }).ToList(),
                OHistory = oHistory.Select(d => new SceneOHistory { OccurredAt = d }).ToList(),
                PlayHistory = viewHistory.Select(d => new ScenePlayHistory { PlayedAt = d }).ToList(),
                RemoteIds = sceneStashIds.GetValueOrDefault(row.Id, [])
                    .Select(s => new SceneRemoteId { Endpoint = s.Ep, RemoteId = s.Rid }).ToList(),
            };

            foreach (var fileId in sceneFiles.GetValueOrDefault(row.Id, []))
            {
                if (!fileData.TryGetValue(fileId, out var fd)) continue;
                if (!videoData.TryGetValue(fileId, out var vd)) continue;
                if (!folderIdMap.TryGetValue(fd.FolderId, out var coveFolderId)) continue;

                scene.Files.Add(new VideoFile
                {
                    Basename = fd.Basename,
                    ParentFolderId = coveFolderId,
                    Size = fd.Size,
                    ModTime = fd.ModTime,
                    CreatedAt = fd.CreatedAt,
                    UpdatedAt = fd.ModTime,
                    Duration = vd.Duration,
                    VideoCodec = vd.VideoCodec,
                    Format = vd.Format,
                    AudioCodec = vd.AudioCodec,
                    Width = vd.Width,
                    Height = vd.Height,
                    FrameRate = vd.FrameRate,
                    BitRate = vd.BitRate,
                    Interactive = vd.Interactive,
                    InteractiveSpeed = vd.InteractiveSpeed,
                    Fingerprints = fingerprints.GetValueOrDefault(fileId, [])
                        .Select(fp => new FileFingerprint { Type = fp.Type, Value = fp.Value }).ToList(),
                });
            }

            _db.Scenes.Add(scene);
            pendingBatch.Add((row.Id, scene));
            count++;

            if (pendingBatch.Count >= SceneBatchSize)
            {
                await _db.SaveChangesAsync(ct);
                FlushSceneBatch();
                _db.ChangeTracker.Clear();
                ReportPhase(progress, startProgress, endProgress, count, sceneRows.Count, $"Importing scenes ({count}/{sceneRows.Count})");
                _logger.LogInformation("Imported {Count}/{Total} scenes...", count, sceneRows.Count);
            }
        }

        if (pendingBatch.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            FlushSceneBatch();
            _db.ChangeTracker.Clear();
            ReportPhase(progress, startProgress, endProgress, count, sceneRows.Count, $"Importing scenes ({count}/{sceneRows.Count})");
        }
        _logger.LogInformation("Imported {Count} scenes", count);

        var generatedMap = new Dictionary<int, SceneGeneratedData>();
        foreach (var row in sceneRows)
        {
            if (!idMap.TryGetValue(row.Id, out var coveId)) continue;
            if (!scenePrimaryFileMap.TryGetValue(row.Id, out var primaryFileId))
            {
                var fileIds = sceneFiles.GetValueOrDefault(row.Id, []);
                if (fileIds.Count == 0) continue;
                primaryFileId = fileIds[0];
            }

            var primaryFingerprints = fingerprints.GetValueOrDefault(primaryFileId, []);
            generatedMap[coveId] = new SceneGeneratedData(
                GetFingerprintValue(primaryFingerprints, "oshash"),
                GetFingerprintValue(primaryFingerprints, "md5"),
                GetBlobId(blobMap, row.CoverBlob));
        }

        return (count, idMap, generatedMap);
    }

    private async Task<int> ImportSceneMarkerSegmentsAsync(
        SqliteConnection conn,
        Dictionary<int, int> sceneIdMap,
        Dictionary<int, int> tagIdMap,
        IJobProgress progress,
        double startProgress,
        double endProgress,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "scene_markers", ct))
        {
            progress.Report(endProgress, "No scene markers to import");
            return 0;
        }

        var total = await CountAsync(conn, "scene_markers", ct);
        if (total == 0)
        {
            progress.Report(endProgress, "No scene markers to import");
            return 0;
        }

        var hasEndSeconds = await ColumnExistsAsync(conn, "scene_markers", "end_seconds", ct);
        var markerRows = new List<(int Id, string Title, double Seconds, double? EndSeconds, int? PrimaryTagId, int? SceneId, string CreatedAt, string UpdatedAt)>();
        await using (var cmd = conn.CreateCommand())
        {
            var endSecondsExpr = hasEndSeconds ? "end_seconds" : "NULL";
            cmd.CommandText = $@"SELECT id, title, seconds, {endSecondsExpr} AS end_seconds, primary_tag_id, scene_id, created_at, updated_at
                FROM scene_markers
                ORDER BY scene_id, seconds, id";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                markerRows.Add((
                    r.GetInt32(0),
                    r.GetString(1),
                    r.GetDouble(2),
                    r.IsDBNull(3) ? null : r.GetDouble(3),
                    ReadIntNull(r, 4),
                    ReadIntNull(r, 5),
                    r.GetString(6),
                    r.GetString(7)));
            }
        }

        var markerTagMap = await TableExistsAsync(conn, "scene_markers_tags", ct)
            ? await ReadJunctionAsync(conn, "scene_markers_tags", "scene_marker_id", "tag_id", ct)
            : new Dictionary<int, List<int>>();

        var legacyAiTagIds = await GetLegacyAiMarkerTagIdsAsync(conn, ct);
        const int MarkerBatchSize = 200;

        var processed = 0;
        var pending = 0;
        var imported = 0;
        var skippedLegacyAi = 0;
        progress.Report(startProgress, "Importing scene marker segments...");

        foreach (var row in markerRows)
        {
            processed++;
            if (!row.PrimaryTagId.HasValue || !row.SceneId.HasValue)
                continue;

            var markerTagIds = markerTagMap.GetValueOrDefault(row.Id, []);
            var allTagIds = new HashSet<int>(markerTagIds) { row.PrimaryTagId.Value };
            if (allTagIds.Any(legacyAiTagIds.Contains))
            {
                skippedLegacyAi++;
                continue;
            }

            if (!sceneIdMap.TryGetValue(row.SceneId.Value, out var coveSceneId))
                continue;
            if (!tagIdMap.TryGetValue(row.PrimaryTagId.Value, out var covePrimaryTagId))
                continue;

            var secondaryTagIds = markerTagIds
                .Where(tagId => tagId != row.PrimaryTagId.Value && tagIdMap.ContainsKey(tagId))
                .Select(tagId => tagIdMap[tagId])
                .Distinct()
                .ToArray();

            _db.Segments.Add(new Segment
            {
                HostType = SegmentHostType.Scene,
                HostId = coveSceneId,
                StartSec = row.Seconds,
                EndSec = row.EndSeconds,
                TagId = covePrimaryTagId,
                Kind = "tag",
                RefId = row.Id,
                Payload = secondaryTagIds.Length > 0 ? JsonSerializer.SerializeToDocument(new { secondaryTagIds }) : null,
                SourceKey = "user",
                Title = string.IsNullOrWhiteSpace(row.Title) ? null : row.Title,
                CreatedAt = ParseDateTime(row.CreatedAt),
                UpdatedAt = ParseDateTime(row.UpdatedAt),
            });

            imported++;
            pending++;

            if (pending >= MarkerBatchSize)
            {
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                pending = 0;
            }

            if (processed % MarkerBatchSize == 0)
                ReportPhase(progress, startProgress, endProgress, processed, markerRows.Count, $"Importing scene marker segments ({processed}/{markerRows.Count})");
        }

        if (pending > 0)
        {
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        ReportPhase(progress, startProgress, endProgress, processed, markerRows.Count, $"Importing scene marker segments ({processed}/{markerRows.Count})");
        _logger.LogInformation("Imported {Count} scene marker segments and skipped {Skipped} legacy AI markers", imported, skippedLegacyAi);
        return imported;
    }

    private async Task<(int aiRunCount, int segmentCount)> ImportAiTagDataAsync(
        string? aiDataSource,
        Dictionary<int, int> sceneIdMap,
        Dictionary<int, int> imageIdMap,
        Dictionary<int, int> tagIdMap,
        IReadOnlyDictionary<string, int> tagNameToCoveIdMap,
        IJobProgress progress,
        double startProgress,
        double endProgress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(aiDataSource))
        {
            progress.Report(endProgress, "Skipping AI tag data import");
            return (0, 0);
        }

        progress.Report(startProgress, "Opening AI tag data source...");
        await using var conn = await OpenAiImportConnectionAsync(aiDataSource, ct);

        if (!await AiTableExistsAsync(conn, "ai_model_runs", ct))
            throw new InvalidOperationException("The supplied AI data source does not contain the ai_model_runs table.");
        if (!await AiTableExistsAsync(conn, "ai_result_timespans", ct))
            throw new InvalidOperationException("The supplied AI data source does not contain the ai_result_timespans table.");

        var runModels = await ReadAiRunModelsAsync(conn, ct);
        var totalRuns = await CountAiRowsAsync(conn, "ai_model_runs", null, ct);
        var totalTimespans = await CountAiRowsAsync(conn, "ai_result_timespans", "payload_type = 'tag'", ct);
        var midProgress = startProgress + ((endProgress - startProgress) * 0.4d);

        var importedRunKeys = new Dictionary<int, string>();
        var importedRunCreatedAt = new Dictionary<int, DateTime>();
        var processedRuns = 0;
        var importedRuns = 0;
        const int AiRunBatchSize = 200;
        progress.Report(startProgress, "Importing AI runs...");

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, service, plugin_name, entity_type, entity_id, status, input_params, started_at, completed_at, result_metadata
                FROM ai_model_runs
                ORDER BY id";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                processedRuns++;

                var legacyRunId = ReadDbIntNull(r, 0);
                var service = ReadDbStringNull(r, 1);
                var pluginName = ReadDbStringNull(r, 2);
                var entityType = ReadDbStringNull(r, 3);
                var legacyEntityId = ReadDbIntNull(r, 4);
                var status = ReadDbStringNull(r, 5);
                var inputParamsJson = ReadDbStringNull(r, 6);
                var startedAt = ReadDbDateTimeNull(r, 7) ?? DateTime.UtcNow;
                var completedAt = ReadDbDateTimeNull(r, 8);
                var resultMetadataJson = ReadDbStringNull(r, 9);

                if (!legacyRunId.HasValue || !legacyEntityId.HasValue || string.IsNullOrWhiteSpace(entityType))
                    continue;

                if (!TryMapAiRunTarget(entityType, legacyEntityId.Value, sceneIdMap, imageIdMap, out var targetType, out var targetId))
                    continue;

                var runKey = BuildImportedAiRunKey(legacyRunId.Value);
                importedRunKeys[legacyRunId.Value] = runKey;
                importedRunCreatedAt[legacyRunId.Value] = startedAt;

                var modelRows = runModels.GetValueOrDefault(legacyRunId.Value, []);
                _db.AiRuns.Add(new AiRun
                {
                    RunKey = runKey,
                    SourceKey = "import:stash-ai-server",
                    TargetType = targetType,
                    TargetId = targetId,
                    Trigger = FirstNonEmpty(pluginName, service),
                    Status = MapAiRunStatus(status),
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    LoadPolicy = ExtractJsonString(inputParamsJson, "load_policy"),
                    FrameIntervalSec = ResolveAiFrameInterval(inputParamsJson, resultMetadataJson, modelRows),
                    Vr = ExtractJsonBool(inputParamsJson, "vr") ?? ExtractJsonBool(resultMetadataJson, "vr"),
                    Request = ParseJsonDocumentOrNull(inputParamsJson),
                    Models = BuildImportedAiModelsDocument(modelRows),
                    Summary = BuildImportedAiSummaryDocument(legacyRunId.Value, service, pluginName, resultMetadataJson),
                    Error = ExtractJsonString(resultMetadataJson, "error"),
                    CreatedAt = startedAt,
                    UpdatedAt = completedAt ?? startedAt,
                });
                importedRuns++;

                if (importedRuns % AiRunBatchSize == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    _db.ChangeTracker.Clear();
                }

                if (processedRuns % AiRunBatchSize == 0)
                    ReportPhase(progress, startProgress, midProgress, processedRuns, totalRuns, $"Importing AI runs ({processedRuns}/{totalRuns})");
            }
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        ReportPhase(progress, startProgress, midProgress, processedRuns, totalRuns, $"Importing AI runs ({processedRuns}/{totalRuns})");

        var hasTimespanCreatedAt = await AiColumnExistsAsync(conn, "ai_result_timespans", "created_at", ct);
        var createdAtExpr = hasTimespanCreatedAt ? "created_at" : "NULL";
        var processedTimespans = 0;
        var importedSegments = 0;
        const int AiSegmentBatchSize = 500;
        progress.Report(midProgress, "Importing AI tag segments...");

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"SELECT id, run_id, entity_type, entity_id, category, str_value, value_id, start_s, end_s, value_json, {createdAtExpr} AS created_at
                FROM ai_result_timespans
                WHERE payload_type = 'tag'
                ORDER BY run_id, entity_id, start_s, id";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                processedTimespans++;

                var legacyTimespanId = ReadDbLongNull(r, 0);
                var legacyRunId = ReadDbIntNull(r, 1);
                var entityType = ReadDbStringNull(r, 2);
                var legacyEntityId = ReadDbIntNull(r, 3);
                var category = ReadDbStringNull(r, 4);
                var strValue = ReadDbStringNull(r, 5);
                var legacyTagId = ReadDbIntNull(r, 6);
                var startSec = ReadDbDoubleNull(r, 7);
                var endSec = ReadDbDoubleNull(r, 8);
                var valueJson = ReadDbStringNull(r, 9);
                var createdAt = ReadDbDateTimeNull(r, 10);

                if (!legacyRunId.HasValue || !legacyEntityId.HasValue || !startSec.HasValue || string.IsNullOrWhiteSpace(entityType))
                    continue;
                if (!importedRunKeys.TryGetValue(legacyRunId.Value, out var runKey))
                    continue;
                if (!TryMapAiSegmentHost(entityType, legacyEntityId.Value, sceneIdMap, imageIdMap, out var hostType, out var hostId))
                    continue;
                if (!TryResolveImportedTagId(legacyTagId, strValue, tagIdMap, tagNameToCoveIdMap, out var coveTagId))
                    continue;

                var timestamp = createdAt ?? importedRunCreatedAt.GetValueOrDefault(legacyRunId.Value, DateTime.UtcNow);
                _db.Segments.Add(new Segment
                {
                    HostType = hostType,
                    HostId = hostId,
                    StartSec = startSec.Value,
                    EndSec = endSec,
                    TagId = coveTagId,
                    Kind = "tag",
                    RefId = legacyTimespanId,
                    Payload = BuildImportedAiSegmentPayload(category, strValue, valueJson),
                    SourceKey = "import:stash-ai-server",
                    SourceRunId = runKey,
                    Confidence = ExtractJsonFloat(valueJson, "confidence"),
                    CreatedAt = timestamp,
                    UpdatedAt = timestamp,
                });
                importedSegments++;

                if (importedSegments % AiSegmentBatchSize == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    _db.ChangeTracker.Clear();
                }

                if (processedTimespans % AiSegmentBatchSize == 0)
                    ReportPhase(progress, midProgress, endProgress, processedTimespans, totalTimespans, $"Importing AI tag segments ({processedTimespans}/{totalTimespans})");
            }
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        ReportPhase(progress, midProgress, endProgress, processedTimespans, totalTimespans, $"Importing AI tag segments ({processedTimespans}/{totalTimespans})");
        _logger.LogInformation("Imported {RunCount} AI runs and {SegmentCount} AI segments from {DataSource}", importedRuns, importedSegments, aiDataSource);
        return (importedRuns, importedSegments);
    }
}