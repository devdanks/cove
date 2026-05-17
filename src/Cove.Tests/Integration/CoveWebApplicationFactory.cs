using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Entities.Auth;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cove.Tests.Integration;

public sealed class CoveWebApplicationFactory : WebApplicationFactory<Program>
{
    public const int TestUserId = 1;

    private readonly string _environmentName;
    private readonly int _port = ReserveLoopbackPort();
    private readonly string _connectionString = $"Data Source=file:cove-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _connection;
    private bool _serverStarted;

    public CoveWebApplicationFactory(string environmentName = "IntegrationTest")
    {
        _environmentName = environmentName;
        _connection = CreateOpenConnection(_connectionString);
        UseKestrel(_port);
        ClientOptions.BaseAddress = new Uri($"http://127.0.0.1:{_port}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cove:Auth:Enabled"] = "true",
                ["Cove:Auth:JwtSecret"] = "integration-test-secret",
                ["Cove:Postgres:Managed"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITokenService>();
            services.RemoveAll<CoveContext>();
            services.RemoveAll<DbContextOptions<CoveContext>>();
            services.RemoveAll<DbContext>();

            services.AddScoped<ITokenService, IntegrationTestTokenService>();
            services.AddScoped(_ => new DbContextOptionsBuilder<CoveContext>()
                .UseSqlite(_connectionString)
                .Options);
            services.AddScoped<CoveContext>(sp => new IntegrationTestCoveContext(
                sp.GetRequiredService<DbContextOptions<CoveContext>>(),
                sp.GetRequiredService<ICurrentPrincipalAccessor>()));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<CoveContext>());
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        EnsureServerStarted();

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "integration-test-token");
        return client;
    }

    public async Task ResetDatabaseAsync()
    {
        EnsureServerStarted();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User
        {
            Id = TestUserId,
            Username = "integration-user",
            PasswordHash = "integration-test",
            PasswordAlgo = "integration-test",
            IsActive = true,
            IsSystem = true,
        });
        await db.SaveChangesAsync();
    }

    public async Task WithDbContextAsync(Func<CoveContext, Task> action)
    {
        EnsureServerStarted();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        await action(db);
    }

    public async Task<TResult> WithDbContextAsync<TResult>(Func<CoveContext, Task<TResult>> action)
    {
        EnsureServerStarted();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        return await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }

    private static SqliteConnection CreateOpenConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void EnsureServerStarted()
    {
        if (_serverStarted)
            return;

        StartServer();
        var addresses = Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;
        var address = addresses?.LastOrDefault();
        if (!string.IsNullOrWhiteSpace(address))
            ClientOptions.BaseAddress = new Uri(address);

        _serverStarted = true;
    }
}

file sealed class IntegrationTestCoveContext(DbContextOptions<CoveContext> options, ICurrentPrincipalAccessor principalAccessor)
    : CoveContext(options, principalAccessor)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

    }
}

file sealed class IntegrationTestTokenService : ITokenService
{
    private static readonly CovePrincipal Principal = new()
    {
        UserId = CoveWebApplicationFactory.TestUserId,
        Username = "integration-user",
        Kind = PrincipalKind.User,
        Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Permissions.All,
        },
    };

    public Task<TokenPair> IssueForUserAsync(int userId, string? ip, string? userAgent, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<TokenPair> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task RevokeChainAsync(string refreshToken, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RevokeAllForUserAsync(int userId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<CovePrincipal?> ResolveAsync(string? authorizationHeader, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return Task.FromResult<CovePrincipal?>(null);

        return Task.FromResult<CovePrincipal?>(Principal);
    }

    public Task<ApiTokenIssued> CreateApiTokenAsync(int userId, string name, IEnumerable<string>? scope, DateTime? expiresAt, CovePrincipal? actor, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task RevokeApiTokenAsync(Guid id, CovePrincipal? actor, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ApiTokenDto>> ListApiTokensAsync(int userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ApiTokenDto>>([]);
}
