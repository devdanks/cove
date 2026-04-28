using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Cove.Api.Hubs;
using Cove.Api.Services;
using Cove.Core.Entities.Galleries;
using Cove.Core.Events;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Plugins;

// Ensure enough threads for async I/O under concurrent load
ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.Sink(new SignalRLogSink()));

    // Bind configuration
    var coveConfig = builder.Configuration.GetSection("Cove");
    builder.Services.Configure<CoveConfiguration>(coveConfig);
    builder.Services.Configure<AuthConfig>(coveConfig.GetSection("Auth"));
    builder.Services.Configure<PostgresConfig>(coveConfig.GetSection("Postgres"));

    // Register a singleton CoveConfiguration instance so all consumers share the same mutable object
    var coveCfgInstance = coveConfig.Get<CoveConfiguration>() ?? new CoveConfiguration();
    builder.Services.AddSingleton(coveCfgInstance);

    // Database - EF Core + PostgreSQL
    var pgSection = coveConfig.GetSection("Postgres");
    var connectionString = pgSection.GetValue<string>("ConnectionString");
    if (string.IsNullOrEmpty(connectionString))
    {
        // Build from individual settings (managed or external)
        var pgPort = pgSection.GetValue<int?>("Port") ?? 5433;
        var pgDb = pgSection.GetValue<string>("Database") ?? "cove";
        connectionString = $"Host=127.0.0.1;Port={pgPort};Database={pgDb};Username=postgres;Trust Server Certificate=true;Minimum Pool Size=10;Maximum Pool Size=200;Timeout=15;Command Timeout=30";
    }
    coveCfgInstance.DatabaseConnectionString = connectionString;
    builder.Services.AddCoveData(connectionString);

    // Event bus (singleton for cross-service communication)
    builder.Services.AddSingleton<IEventBus, EventBus>();

    // Job service (background task processing)
    builder.Services.AddSingleton<JobService>();
    builder.Services.AddSingleton<IJobService>(sp => sp.GetRequiredService<JobService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<JobService>());

    // Gallery services (zip reading, gallery parsing, etc.)
    builder.Services.AddGalleryServices();

    // Application services
    builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();
    builder.Services.AddSingleton<IFingerprintService, FingerprintService>();
    builder.Services.AddScoped<IScanService, ScanService>();
    builder.Services.AddScoped<IStreamService, StreamService>();
    builder.Services.AddScoped<IAutoTagService, AutoTagService>();
    builder.Services.AddScoped<ICleanService, CleanService>();
    builder.Services.AddScoped<IBackupService, BackupService>();
    builder.Services.AddSingleton<IBlobService, BlobService>();
    builder.Services.AddSingleton<ConfigService>();
    builder.Services.AddSingleton<AuthBypassPrincipalProvider>();
    builder.Services.AddSingleton<ScraperService>();
    builder.Services.AddSingleton<ISceneCoverService, SceneCoverService>();
    builder.Services.AddScoped<ISceneMetadataApplyService, SceneMetadataApplyService>();
    builder.Services.AddScoped<PerformerScrapeService>();
    builder.Services.AddScoped<ScrapeAttemptService>();
    builder.Services.AddScoped<SceneBatchScrapeService>();
    builder.Services.AddSingleton<DownloaderService>();
    builder.Services.AddSingleton<ITranscodeService, TranscodeService>();
    builder.Services.AddScoped<StashMigrationService>();
    builder.Services.AddHttpClient("scraper", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        client.Timeout = TimeSpan.FromSeconds(45);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        UseCookies = true,
        CookieContainer = new System.Net.CookieContainer(),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    });
    builder.Services.AddHttpClient<MetadataServerService>();

    // Extension system
    var extensionsDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cove", "extensions");
    Directory.CreateDirectory(extensionsDataDir);
    var extensionContext = new ExtensionContext
    {
        Configuration = builder.Configuration,
        DataDirectory = extensionsDataDir,
        CoveVersion = "0.0.1"
    };
    var extensionManager = new ExtensionManager(extensionContext);
    // Discover .NET plugin DLLs from extensions directory
    extensionManager.DiscoverExtensions(extensionsDataDir);
    // Register built-in extensions
    extensionManager.Register(new Cove.Api.Extensions.ThemeCollectionExtension());
    extensionManager.Register(new Cove.Api.Extensions.AuditLogExtension());
    extensionManager.Register(new Cove.Api.Extensions.DirectFileDownloaderExtension());
    CoveContext.SetDataExtensions(extensionManager.Extensions.OfType<IDataExtension>());
    builder.Services.AddSingleton(extensionManager);
    builder.Services.AddSingleton<IExtensionStoreFactory>(sp => new Cove.Data.Repositories.EfExtensionStoreFactory(sp));
    builder.Services.AddSingleton<IExtensionRegistry>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient("ExtensionRegistry");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Cove/1.0");
        return new GitHubExtensionRegistry(http);
    });
    builder.Services.AddHttpClient("ExtensionRegistry");
    builder.Services.AddHostedService<ExtensionEventBridge>();
    extensionManager.ConfigureServices(builder.Services);

    // Managed PostgreSQL — auto-downloads and runs a local PG instance
    var pgManaged = pgSection.GetValue<bool?>("Managed") ?? true;
    if (pgManaged)
        builder.Services.AddHostedService<PostgresManagerService>();

    // Auth bootstrap (must run AFTER PostgresManagerService so the DB is reachable).
    builder.Services.AddHostedService<Cove.Data.Auth.BootstrapAuthService>();

    // FFmpeg — auto-downloads if not found in PATH or configured path
    builder.Services.AddHostedService<FfmpegManagerService>();

    // SignalR
    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    // Auth
    var authConfig = coveConfig.GetSection("Auth");
    var jwtSecret = authConfig.GetValue<string>("JwtSecret") ?? Guid.NewGuid().ToString();
    var authEnabled = authConfig.GetValue<bool>("Enabled");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "Cove",
                ValidAudience = "Cove",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
            };
            // Allow SignalR to authenticate via query string
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });
    builder.Services.AddAuthorization();

    // MVC + OpenAPI
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<Cove.Api.Middleware.EntityEventFilter>();
        options.Filters.Add<Cove.Api.Middleware.AuthExceptionFilter>();
        options.Filters.Add<Cove.Api.Middleware.PermissionAuthorizationFilter>();
        options.Filters.Add<Cove.Api.Middleware.EntityAccessActionFilter>();
    })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
    builder.Services.AddOpenApi();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Response compression â€” reduces 22KB scene lists to ~2KB
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

    // Output caching for read-heavy API endpoints
    builder.Services.AddOutputCache(options =>
    {
        options.AddBasePolicy(b => b.NoCache());
        options.AddPolicy("ShortCache", b => b
            .AddPolicy<Cove.Api.Middleware.AuthAwareOutputCachePolicy>()
            .Expire(TimeSpan.FromSeconds(1))
            .SetVaryByHeader("Authorization", "Cookie", "X-Share-Token", "X-Share-Password")
            .SetLocking(false), true);
    });

    // In-memory cache for POST query results
    builder.Services.AddMemoryCache();

    // Rate limiting — tight bucket on auth endpoints to slow brute-force; lenient global default.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Tight: /api/auth/login + /api/auth/refresh — 10 requests / 15s sliding window per IP+username.
        options.AddPolicy("auth-strict", httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = ip;
            // Best-effort: combine with submitted username so two users behind a NAT don't lock each other.
            if (httpContext.Request.HasJsonContentType() && httpContext.Request.ContentLength is > 0 and < 4096)
            {
                // body cannot be read here without buffering; key on IP only.
            }
            return System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
                new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromSeconds(15),
                    SegmentsPerWindow = 3,
                    QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                });
        });
    });

    // CORS - allow frontend dev server
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Middleware pipeline
    // UseSerilogRequestLogging removed â€” adds 3-5ms per request overhead

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseResponseCompression();
    app.UseCors();
    app.UseOutputCache();
    app.UseRateLimiter();

    // Extension middleware (runs before auth, after CORS)
    extensionManager.ConfigureMiddleware(app);

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Always run our principal-resolver middleware so [RequiresPermission] can read it.
    // (When auth is disabled the filter short-circuits and treats requests as anonymous-allowed.)
    app.UseMiddleware<Cove.Api.Middleware.CurrentPrincipalMiddleware>();

    app.MapControllers();
    app.MapHub<JobHub>("/hubs/jobs");
    app.MapHub<LogHub>("/hubs/logs");
    extensionManager.MapEndpoints(app);

    // Serve SPA static files (production)
    // When running as a single-file executable, wwwroot is embedded as managed resources.
    // Fall back to the embedded file provider when the physical wwwroot folder is absent.
    var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    IFileProvider? spaFileProvider = null;
    if (Directory.Exists(webRootPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }
    else
    {
        spaFileProvider = new ManifestEmbeddedFileProvider(
            typeof(Program).Assembly, "wwwroot");
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = spaFileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = spaFileProvider });
    }

    if (spaFileProvider != null)
    {
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = spaFileProvider });
    }
    else
    {
        app.MapFallbackToFile("index.html");
    }

    var port = coveConfig.GetValue<int?>("Port") ?? 9999;
    app.Urls.Add($"http://0.0.0.0:{port}");

    // Initialize SignalR log sink with hub context
    SignalRLogSink.SetHubContext(app.Services.GetRequiredService<IHubContext<LogHub>>());

    // Start hosted services (including managed PostgreSQL) before database creation.
    await app.StartAsync();

    // Load saved user config (cove-config.json) and apply on top of appsettings.json
    var configSvc = app.Services.GetRequiredService<ConfigService>();
    var savedConfig = await configSvc.LoadSavedConfigAsync();
    if (savedConfig != null)
    {
        await configSvc.SaveConfigAsync(savedConfig); // applies to live IOptions
        Log.Information("Loaded user configuration from {Path}", configSvc.ConfigPath);
    }

    // Auto-migrate database + pre-warm EF Core and connection pool
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();

        // Determine if this is a brand-new database or an existing one that predates migrations.
        var canConnect = await db.Database.CanConnectAsync();
        var hasMigrationHistory = false;
        if (canConnect)
        {
            // Check if __EFMigrationsHistory table exists (indicates migrations-aware DB)
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='__EFMigrationsHistory'";
            hasMigrationHistory = await cmd.ExecuteScalarAsync() != null;
            await conn.CloseAsync();
        }

        var hasTables = false;
        if (canConnect && !hasMigrationHistory)
        {
            // Check if core tables exist (pre-migration database)
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='scenes'";
            hasTables = await cmd.ExecuteScalarAsync() != null;
            await conn.CloseAsync();
        }

        if (hasTables && !hasMigrationHistory)
        {
            // Existing database created with EnsureCreatedAsync — baseline it.
            // Mark the initial migration as already applied so MigrateAsync only runs future migrations.
            Log.Information("Existing database detected — baselining migration history");
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();

            await using var createHistory = conn.CreateCommand();
            createHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                )
            """;
            await createHistory.ExecuteNonQueryAsync();

            await using var insertBaseline = conn.CreateCommand();
            insertBaseline.CommandText = """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260419000753_InitialCreate', '10.0.5')
                ON CONFLICT DO NOTHING
            """;
            await insertBaseline.ExecuteNonQueryAsync();
            await conn.CloseAsync();

            // Still run compatibility patches for pre-migration databases
            await EnsureColumnsAsync(db);

            Log.Information("Migration history baselined — future migrations will apply automatically");
        }

        // Check for pending migrations
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();

        if (pendingMigrations.Length > 0)
        {
            Log.Information("Applying {Count} pending migration(s): {Migrations}",
                pendingMigrations.Length, string.Join(", ", pendingMigrations));

            // Automatic backup before migration
            var backupSvc = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var backup = await backupSvc.CreateBackupAsync("pre_migration");
            Log.Information("Pre-migration backup created at {Path}", backup.BackupPath);

            await db.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
        }
        else if (!hasTables && !hasMigrationHistory)
        {
            // Brand new database — apply all migrations from scratch
            Log.Information("New database detected — applying all migrations");
            await db.Database.MigrateAsync();
            Log.Information("Database created via migrations");
        }
        else
        {
            Log.Information("Database is up to date");
        }

        // Compatibility columns must exist before any startup queries touch the model.
        await EnsureColumnsAsync(db);

        // Fix oshash values: Go uses %016x (zero-padded 16 chars), ensure all values match
        await NormalizeOshashValuesAsync(db);

        // Pre-warm: compile EF Core query cache, prime connection pool, JIT hot paths
        _ = await db.Scenes.CountAsync();
        _ = await db.Scenes.AsNoTracking()
            .OrderBy(s => s.Id)
            .Include(s => s.Files).ThenInclude(f => f.Fingerprints)
            .Include(s => s.SceneTags).ThenInclude(st => st.Tag)
            .Include(s => s.ScenePerformers).ThenInclude(sp => sp.Performer)
            .Take(1).AsSplitQuery().ToListAsync();
        Log.Information("EF Core and connection pool pre-warmed");
    }

    // Initialize extensions after database is ready
    await extensionManager.InitializeAllAsync(app.Services);

    // Collect extension-contributed permissions and content policies (auth integration).
    {
        var permissionRegistry = app.Services.GetRequiredService<Cove.Core.Auth.IPermissionRegistry>();
        foreach (var ext in extensionManager.Extensions)
        {
            if (ext is Cove.Sdk.IPermissionContributor pc)
            {
                try
                {
                    var contributed = pc.ContributePermissions().ToList();
                    permissionRegistry.RegisterExtensionPermissions(ext.Id, contributed);
                    Log.Information("Extension {Id} contributed {Count} permission(s)", ext.Id, contributed.Count);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Extension {Id} failed to contribute permissions", ext.Id);
                }
            }
            if (ext is Cove.Sdk.IContentPolicyContributor cp)
            {
                try
                {
                    var policies = cp.ContributePolicies();
                    Log.Information("Extension {Id} contributed {Count} content policy/policies", ext.Id, policies.Count);
                    // Policies are recorded for audit/inspection. Full enforcement at the EF
                    // query-filter layer is part of Schema C Stage 2; v1 stores them so the
                    // surface is documented and the auth services can consult them.
                    Cove.Core.Auth.ContentPolicyRegistry.Register(ext.Id, policies);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Extension {Id} failed to contribute content policies", ext.Id);
                }
            }
        }
    }

    Log.Information("Cove starting on port {Port}", port);
    await app.WaitForShutdownAsync();

    // Graceful shutdown for extensions
    await extensionManager.ShutdownAllAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task EnsureColumnsAsync(CoveContext db)
{
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();

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

    async Task AddColumnIfMissing(string table, string column, string type, string? defaultValue = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_name='{table}' AND column_name='{column}'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists != null) return;

        var def = defaultValue != null ? $" DEFAULT {defaultValue}" : "";
        await using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}{def}";
        await alter.ExecuteNonQueryAsync();
    }

    async Task EnsureTableExists(string table, string createSql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='{table}'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists != null) return;

        await using var create = conn.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }

    // Gallery and scene cover image support
    await AddColumnIfMissing("scenes", "ImageBlobId", "text");
    await AddColumnIfMissing("galleries", "ImageBlobId", "text");
    await AddColumnIfMissing("galleries", "CoverImageId", "integer");
    await AddColumnIfMissing("scrape_attempts", "CandidateResultsJson", "text");

    // Remote ID support added after the initial database schema.
    await EnsureTableExists("SceneRemoteId", """
        CREATE TABLE IF NOT EXISTS "SceneRemoteId" (
            "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "SceneId" integer NOT NULL REFERENCES "scenes"("Id") ON DELETE CASCADE,
            "Endpoint" text NOT NULL,
            "RemoteId" text NOT NULL
        )
    """);
    await EnsureTableExists("PerformerRemoteId", """
        CREATE TABLE IF NOT EXISTS "PerformerRemoteId" (
            "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "PerformerId" integer NOT NULL REFERENCES "performers"("Id") ON DELETE CASCADE,
            "Endpoint" text NOT NULL,
            "RemoteId" text NOT NULL
        )
    """);
    await EnsureTableExists("TagRemoteId", """
        CREATE TABLE IF NOT EXISTS "TagRemoteId" (
            "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "TagId" integer NOT NULL REFERENCES "tags"("Id") ON DELETE CASCADE,
            "Endpoint" text NOT NULL,
            "RemoteId" text NOT NULL
        )
    """);
    await EnsureTableExists("StudioRemoteId", """
        CREATE TABLE IF NOT EXISTS "StudioRemoteId" (
            "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "StudioId" integer NOT NULL REFERENCES "studios"("Id") ON DELETE CASCADE,
            "Endpoint" text NOT NULL,
            "RemoteId" text NOT NULL
        )
    """);

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

    // Extension key-value storage (added after initial schema)
    await EnsureTableExists("extension_data", """
        CREATE TABLE IF NOT EXISTS "extension_data" (
            "ExtensionId" text NOT NULL,
            "Key" text NOT NULL,
            "Value" text NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            PRIMARY KEY ("ExtensionId", "Key")
        )
    """);

    await using (var extIndex = conn.CreateCommand())
    {
        extIndex.CommandText = "CREATE INDEX IF NOT EXISTS \"IX_extension_data_ExtensionId\" ON \"extension_data\" (\"ExtensionId\")";
        await extIndex.ExecuteNonQueryAsync();
    }

    // Video captions support
    // Create the table if it does not exist before checking for missing columns.
    await EnsureTableExists("VideoCaptions", """
        CREATE TABLE IF NOT EXISTS "VideoCaptions" (
            "Id" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "FileId" integer NOT NULL REFERENCES "files"("Id") ON DELETE CASCADE,
            "LanguageCode" text NOT NULL DEFAULT '00',
            "CaptionType" text NOT NULL DEFAULT 'vtt',
            "Filename" text NOT NULL
        )
    """);

    await AddColumnIfMissing("VideoCaptions", "Id", "integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY");

    // FK indexes on the files table for faster lookups (e.g. streaming by SceneId)
    var fileIndexCommands = new[]
    {
        """CREATE INDEX IF NOT EXISTS "IX_files_SceneId" ON "files" ("SceneId")""",
        """CREATE INDEX IF NOT EXISTS "IX_files_ImageId" ON "files" ("ImageId")""",
    };
    foreach (var sql in fileIndexCommands)
    {
        await using var indexCmd = conn.CreateCommand();
        indexCmd.CommandText = sql;
        await indexCmd.ExecuteNonQueryAsync();
    }

    await conn.CloseAsync();
}

/// <summary>
/// Normalize oshash fingerprint values to match Go's fmt.Sprintf("%016x") format:
/// zero-padded to exactly 16 hex characters. Previously the C# implementation
/// used unpadded hex which caused metadata-server matching failures.
/// </summary>
static async Task NormalizeOshashValuesAsync(CoveContext db)
{
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();

    // Fix oshash values â€” SQLite uses substr/replace for zero-padding
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        UPDATE "FileFingerprints"
        SET "Value" = substr('0000000000000000' || "Value", -16, 16)
        WHERE "Type" = 'oshash' AND length("Value") < 16
    """;
    var affected = await cmd.ExecuteNonQueryAsync();

    // Ensure join table indexes exist for filter performance (EF migrations may not create them for existing DBs)
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
        catch { /* index may already exist */ }
    }

    await conn.CloseAsync();

    if (affected > 0)
        Log.Information("Normalized {Count} oshash fingerprint values to 16-char padded format", affected);
}
