using Cove.Api.Services;
using Cove.Core.DTOs;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using DatabaseController = Cove.Api.Controllers.DatabaseController;

namespace Cove.Tests;

[CollectionDefinition("Managed Postgres integration", DisableParallelization = true)]
public sealed class ManagedPostgresIntegrationCollection;

[Collection("Managed Postgres integration")]
public class BackupServiceIntegrationTests
{
    [Fact]
    public async Task ConfigBackupAndRestore_RestoresConfigAndCreatesPreRestoreSnapshot()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"cove_config_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);

        try
        {
            var configPath = Path.Combine(dataRoot, "cove-config.json");
            await File.WriteAllTextAsync(configPath, "{\"title\":\"before\"}");

            var service = new BackupService(new NullJobService(), new CoveConfiguration(), NullLogger<BackupService>.Instance, dataRoot);
            var backup = await service.CreateConfigBackupAsync("integration_test");

            Assert.NotNull(backup);
            Assert.True(File.Exists(backup.BackupPath));
            Assert.True(backup.SizeBytes > 0);

            await File.WriteAllTextAsync(configPath, "{\"title\":\"after\"}");

            await service.RestoreConfigBackupAsync(backup.BackupPath);

            Assert.Equal("{\"title\":\"before\"}", await File.ReadAllTextAsync(configPath));
            var latestBackup = await service.GetLatestConfigBackupPathAsync();
            Assert.NotNull(latestBackup);
            Assert.Contains("pre_restore", Path.GetFileName(latestBackup));
            Assert.Equal("{\"title\":\"after\"}", await File.ReadAllTextAsync(latestBackup));
        }
        finally
        {
            try
            {
                Directory.Delete(dataRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task RestoreDatabase_CreatesPreRestoreBackupBeforeRestore()
    {
        var backupService = new RecordingBackupService();
        var controller = new DatabaseController(null!, backupService, NullLogger<DatabaseController>.Instance);

        var action = await controller.RestoreDatabase(new RestoreBackupRequestDto("restore.sql"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<RestoreBackupResultDto>(ok.Value);
        Assert.Equal("restore.sql", response.BackupPath);
        Assert.Equal("pre-restore.sql", response.PreRestoreBackupPath);
        Assert.Equal(["backup:pre_restore", "restore:restore.sql"], backupService.Calls);
    }

    [Fact]
    public async Task CreateBackupAndRestore_RestoresDatabaseToBackupPoint()
    {
        var managedRoot = ResolveManagedPostgresRoot();
        if (managedRoot == null)
            return;

        var databaseName = $"backup_verify_{Guid.NewGuid():N}";
        var postgresConfig = new PostgresConfig
        {
            Managed = true,
            DataPath = managedRoot,
            Port = 5547,
            Database = databaseName,
        };
        var config = new CoveConfiguration
        {
            Postgres = postgresConfig,
        };

        var manager = new PostgresManagerService(Options.Create(postgresConfig), NullLogger<PostgresManagerService>.Instance);
        await manager.StartAsync(CancellationToken.None);

        string? backupPath = null;

        try
        {
            var connectionString = $"Host=127.0.0.1;Port={postgresConfig.Port};Database={databaseName};Username=postgres;Trust Server Certificate=true;Timeout=15;Command Timeout=30";

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();

                await ExecuteNonQueryAsync(conn, "CREATE TABLE IF NOT EXISTS backup_probe (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL);");
                await ExecuteNonQueryAsync(conn, "INSERT INTO backup_probe (name) VALUES ('before backup');");
            }

            var service = new BackupService(new NullJobService(), config, NullLogger<BackupService>.Instance);
            var backup = await service.CreateBackupAsync("integration_test");
            backupPath = backup.BackupPath;

            Assert.True(File.Exists(backupPath));
            Assert.True(backup.SizeBytes > 0);
            Assert.Equal(backupPath, await service.GetLatestBackupPathAsync());

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                await ExecuteNonQueryAsync(conn, "INSERT INTO backup_probe (name) VALUES ('after backup');");
                var namesBeforeRestore = await ReadProbeNamesAsync(conn);
                Assert.Equal(["before backup", "after backup"], namesBeforeRestore);
            }

            await service.RestoreBackupAsync(backupPath);

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                var namesAfterRestore = await ReadProbeNamesAsync(conn);
                Assert.Equal(["before backup"], namesAfterRestore);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                File.Delete(backupPath);

            await manager.StopAsync(CancellationToken.None);
        }
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadProbeNamesAsync(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM backup_probe ORDER BY id";

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        return [.. names];
    }

    private static string? ResolveManagedPostgresRoot()
    {
        var repoArtifactRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "backup-verify-data");
        if (File.Exists(Path.Combine(repoArtifactRoot, "pgsql", "bin", Exe("pg_ctl"))))
            return repoArtifactRoot;

        var localAppDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cove");
        if (File.Exists(Path.Combine(localAppDataRoot, "pgsql", "bin", Exe("pg_ctl"))))
            return localAppDataRoot;

        return null;
    }

    private static string Exe(string toolName)
    {
        return OperatingSystem.IsWindows() ? toolName + ".exe" : toolName;
    }

    private sealed class NullJobService : IJobService
    {
        public string Enqueue(string type, string description, Func<IJobProgress, CancellationToken, Task> work, bool exclusive = true)
            => throw new NotSupportedException();

        public bool Cancel(string jobId) => false;

        public bool ReorderQueued(string jobId, string? beforeJobId) => false;

        public JobInfo? GetJob(string jobId) => null;

        public IReadOnlyList<JobInfo> GetAllJobs() => [];

        public IReadOnlyList<JobInfo> GetJobHistory() => [];
    }

    private sealed class RecordingBackupService : IBackupService
    {
        public List<string> Calls { get; } = [];

        public Task<BackupResultDto> CreateBackupAsync(string? reason = null, CancellationToken ct = default)
        {
            Calls.Add($"backup:{reason}");
            return Task.FromResult(new BackupResultDto("pre-restore.sql", 12, "20260419_000000"));
        }

        public string StartBackup() => throw new NotSupportedException();

        public Task RestoreBackupAsync(string backupPath, CancellationToken ct = default)
        {
            Calls.Add($"restore:{backupPath}");
            return Task.CompletedTask;
        }

        public Task<string?> GetLatestBackupPathAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task<ConfigBackupResultDto?> CreateConfigBackupAsync(string? reason = null, CancellationToken ct = default) => Task.FromResult<ConfigBackupResultDto?>(null);

        public Task RestoreConfigBackupAsync(string backupPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> GetLatestConfigBackupPathAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}