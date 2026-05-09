using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Cove.Data.Auth;
using Cove.Data.Repositories;
using Cove.Data.Services;
using Npgsql;
using Pgvector;

namespace Cove.Data;

public static class DataServiceExtensions
{
    public static IServiceCollection AddCoveData(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(sp =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            dataSourceBuilder.UseVector();
            return dataSourceBuilder.Build();
        });

        // Use DbContext pooling for faster context acquisition (avoids repeated setup)
        services.AddDbContextPool<CoveContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();

            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.MigrationsAssembly(typeof(CoveContext).Assembly.FullName);
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            });
            // Disable thread safety checks in production for ~5% faster context operations
            options.EnableThreadSafetyChecks(false);
            // Disable detailed errors (only useful for debugging)
            options.EnableDetailedErrors(false);
        }, poolSize: 256);

        // Allow extensions to resolve via DbContext base type
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<CoveContext>());

        services.AddScoped<ISceneRepository, SceneRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IStudioRepository, StudioRepository>();
        services.AddScoped<IGalleryRepository, GalleryRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<ISavedFilterRepository, SavedFilterRepository>();
        services.AddScoped<EmbeddingService>();
        services.AddScoped<SegmentSpanResolver>();
        services.AddScoped<FacePerformerPropagationService>();
        services.AddScoped<IUserEngagementService, UserEngagementService>();
        services.AddScoped<IEmbeddingService>(sp => sp.GetRequiredService<EmbeddingService>());
        services.AddScoped<ITextEncoderRegistry>(sp => sp.GetRequiredService<EmbeddingService>());

        // Schema C Stage 1 dual-write
        services.AddScoped<IEntityIdentifierService, EntityIdentifierService>();

        // Auth / RBAC services
        services.AddSingleton<IPermissionRegistry, PermissionRegistry>();
        services.AddSingleton<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IContentRuleService, ContentRuleService>();
        services.AddScoped<IShareLinkService, ShareLinkService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<AuditService>());
        services.AddHostedService(sp => sp.GetRequiredService<AuditService>());
        // NOTE: BootstrapAuthService is registered explicitly in Program.cs *after*
        // PostgresManagerService so the managed PostgreSQL instance is up before we
        // try to seed permissions/roles/owner. (Hosted services run sequentially in
        // registration order.)

        return services;
    }
}
