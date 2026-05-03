using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cove.Api.Startup;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cove.Tests;

[Collection("Managed Postgres integration")]
public sealed class Phase12SchemaParityTests
{
    [Fact]
    public async Task Program_AddColumnIfMissing_IsIdempotent()
    {
        var managedRoot = ResolveManagedPostgresRoot();
        if (managedRoot == null)
            return;

        var databaseName = $"phase12_idempotent_{Guid.NewGuid():N}";
        await using var environment = await CreateEnvironmentAsync(managedRoot);
        await CreateDatabaseAsync(environment.AdminConnectionString, databaseName);

        try
        {
            await using (var context = CreateContext(environment.Port, databaseName))
            {
                AssertNoPendingModelChanges(context);
                await context.Database.MigrateAsync();
                await SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaAsync(context);
                await SchemaCompatibilityBootstrap.NormalizeOshashAndIndexesAsync(context);
            }

            var before = await DumpSchemaAsync(environment.PgDumpPath, environment.Port, databaseName);

            await using (var context = CreateContext(environment.Port, databaseName))
            {
                await SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaAsync(context);
                await SchemaCompatibilityBootstrap.NormalizeOshashAndIndexesAsync(context);
            }

            var after = await DumpSchemaAsync(environment.PgDumpPath, environment.Port, databaseName);

            Assert.Equal(before, after);
        }
        finally
        {
            await DropDatabaseAsync(environment.AdminConnectionString, databaseName);
        }
    }

    [Fact]
    public async Task BootstrapUpgradePath_MatchesFreshMigrationSchema()
    {
        var managedRoot = ResolveManagedPostgresRoot();
        if (managedRoot == null)
            return;

        var freshDatabaseName = $"phase12_fresh_{Guid.NewGuid():N}";
        var bootstrapDatabaseName = $"phase12_bootstrap_{Guid.NewGuid():N}";

        await using var environment = await CreateEnvironmentAsync(managedRoot);
        await CreateDatabaseAsync(environment.AdminConnectionString, freshDatabaseName);
        await CreateDatabaseAsync(environment.AdminConnectionString, bootstrapDatabaseName);

        try
        {
            await using (var context = CreateContext(environment.Port, freshDatabaseName))
            {
                AssertNoPendingModelChanges(context);
                await context.Database.MigrateAsync();
                await SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaAsync(context);
                await SchemaCompatibilityBootstrap.NormalizeOshashAndIndexesAsync(context);
            }

            await using (var context = CreateContext(environment.Port, bootstrapDatabaseName))
            {
                AssertNoPendingModelChanges(context);
                await context.Database.MigrateAsync("20260419000753_InitialCreate");
                await SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaAsync(context);
                await context.Database.MigrateAsync();
                await SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaAsync(context);
                await SchemaCompatibilityBootstrap.NormalizeOshashAndIndexesAsync(context);
            }

            var freshSchema = await DumpSchemaAsync(environment.PgDumpPath, environment.Port, freshDatabaseName);
            var bootstrapSchema = await DumpSchemaAsync(environment.PgDumpPath, environment.Port, bootstrapDatabaseName);

            AssertSchemasEqual(freshSchema, bootstrapSchema);
        }
        finally
        {
            await DropDatabaseAsync(environment.AdminConnectionString, freshDatabaseName);
            await DropDatabaseAsync(environment.AdminConnectionString, bootstrapDatabaseName);
        }
    }

    private static CoveContext CreateContext(int port, string databaseName)
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseNpgsql(BuildConnectionString(port, databaseName))
            .Options;

        return new CoveContext(options);
    }

    private static string BuildConnectionString(int port, string databaseName)
        => $"Host=127.0.0.1;Port={port};Database={databaseName};Username=postgres;Trust Server Certificate=true;Timeout=15;Command Timeout=30";

    private static void AssertNoPendingModelChanges(CoveContext context)
    {
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshotModel = initializer.Initialize(snapshot!.Model, designTime: true);
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var operations = differ.GetDifferences(snapshotModel.GetRelationalModel(), designTimeModel.GetRelationalModel());
        if (operations.Count == 0)
            return;

        var details = string.Join(Environment.NewLine, operations.Select(FormatOperation));
        throw new Xunit.Sdk.XunitException($"Pending model changes detected:{Environment.NewLine}{details}");
    }

    private static string FormatOperation(MigrationOperation operation)
        => operation switch
        {
            AddColumnOperation addColumn => $"AddColumn {addColumn.Table}.{addColumn.Name} ({addColumn.ColumnType ?? addColumn.ClrType.Name})",
            AlterColumnOperation alterColumn => $"AlterColumn {alterColumn.Table}.{alterColumn.Name} ({alterColumn.ColumnType ?? alterColumn.ClrType.Name})",
            CreateTableOperation createTable => $"CreateTable {createTable.Name}",
            CreateIndexOperation createIndex => $"CreateIndex {createIndex.Table}.{createIndex.Name}",
            DropColumnOperation dropColumn => $"DropColumn {dropColumn.Table}.{dropColumn.Name}",
            DropIndexOperation dropIndex => $"DropIndex {dropIndex.Table}.{dropIndex.Name}",
            DropTableOperation dropTable => $"DropTable {dropTable.Name}",
            _ => operation.GetType().Name,
        };

    private static async Task<string> DumpSchemaAsync(string pgDumpPath, int port, string databaseName)
    {
        var psi = new ProcessStartInfo(pgDumpPath, $"-h 127.0.0.1 -p {port} -U postgres -d {databaseName} -s --no-owner --no-privileges")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pg_dump.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump failed for {databaseName}: {stderr}");

        return NormalizeSchemaDump(stdout);
    }

    private static string NormalizeSchemaDump(string dump)
    {
        var statements = new List<string>();
        var current = new StringBuilder();

        foreach (var rawLine in dump.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("--", StringComparison.Ordinal)
                || line.StartsWith("SET ", StringComparison.Ordinal)
                || line.StartsWith("SELECT pg_catalog.set_config", StringComparison.Ordinal)
                || line.StartsWith("\\", StringComparison.Ordinal))
            {
                continue;
            }

            current.Append(line).Append(' ');
            if (line.EndsWith(';'))
            {
                statements.Add(CanonicalizeStatement(current.ToString().Trim()));
                current.Clear();
            }
        }

        if (current.Length > 0)
            statements.Add(CanonicalizeStatement(current.ToString().Trim()));

        return string.Join('\n', statements.OrderBy(statement => statement, StringComparer.Ordinal));
    }

    private static string CanonicalizeStatement(string statement)
    {
        if (!statement.StartsWith("CREATE TABLE public.", StringComparison.Ordinal))
            return statement;

        var openParen = statement.IndexOf('(');
        var closeParen = statement.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
            return statement;

        var prefix = statement[..openParen].TrimEnd();
        var suffix = statement[(closeParen + 1)..].Trim();
        var items = SplitTopLevel(statement[(openParen + 1)..closeParen])
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .OrderBy(item => item, StringComparer.Ordinal);

        var rebuilt = $"{prefix} ( {string.Join(", ", items)} )";
        return suffix.Length == 0 ? rebuilt : $"{rebuilt} {suffix}";
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var start = 0;
        var depth = 0;
        var inSingleQuote = false;

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '\'' && (index == 0 || text[index - 1] != '\\'))
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inSingleQuote)
                continue;

            switch (ch)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return text[start..index];
                    start = index + 1;
                    break;
            }
        }

        yield return text[start..];
    }

    private static void AssertSchemasEqual(string expectedSchema, string actualSchema)
    {
        if (string.Equals(expectedSchema, actualSchema, StringComparison.Ordinal))
            return;

        var expectedStatements = expectedSchema.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actualStatements = actualSchema.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var missing = expectedStatements.Except(actualStatements, StringComparer.Ordinal).Take(10).ToArray();
        var extra = actualStatements.Except(expectedStatements, StringComparer.Ordinal).Take(10).ToArray();

        var message = new StringBuilder();
        message.AppendLine("Normalized schema dumps differ.");
        if (missing.Length > 0)
        {
            message.AppendLine("Missing statements:");
            foreach (var statement in missing)
                message.AppendLine(statement);
        }

        if (extra.Length > 0)
        {
            message.AppendLine("Extra statements:");
            foreach (var statement in extra)
                message.AppendLine(statement);
        }

        throw new Xunit.Sdk.XunitException(message.ToString().TrimEnd());
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string adminConnectionString, string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();

        await using (var terminate = conn.CreateCommand())
        {
            terminate.CommandText = $"""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()
            """;
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<PostgresTestEnvironment> CreateEnvironmentAsync(string managedRoot)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = ReserveLoopbackPort();
            var postgresConfig = new PostgresConfig
            {
                Managed = true,
                DataPath = managedRoot,
                Port = port,
                Database = "postgres",
            };

            var manager = new PostgresManagerService(Options.Create(postgresConfig), NullLogger<PostgresManagerService>.Instance);

            try
            {
                await manager.StartAsync(CancellationToken.None);

                var pgDumpPath = Path.Combine(managedRoot, "pgsql", "bin", Exe("pg_dump"));
                return new PostgresTestEnvironment(manager, port, BuildConnectionString(port, "postgres"), pgDumpPath);
            }
            catch (Exception ex) when (attempt < 4)
            {
                lastError = ex;
                try
                {
                    await manager.StopAsync(CancellationToken.None);
                }
                catch
                {
                }
            }
        }

        throw new InvalidOperationException("Failed to start managed Postgres for schema parity tests.", lastError);
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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
        => OperatingSystem.IsWindows() ? toolName + ".exe" : toolName;

    private sealed class PostgresTestEnvironment(PostgresManagerService manager, int port, string adminConnectionString, string pgDumpPath) : IAsyncDisposable
    {
        public int Port { get; } = port;
        public string AdminConnectionString { get; } = adminConnectionString;
        public string PgDumpPath { get; } = pgDumpPath;

        public async ValueTask DisposeAsync()
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }
}