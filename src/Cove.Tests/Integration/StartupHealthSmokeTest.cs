using System.Net;

using Cove.Api.Startup;
using Cove.Core.Entities.Auth;

namespace Cove.Tests.Integration;

public sealed class StartupHealthSmokeTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOk_AfterStartupBootstrapRuns()
    {
        SchemaCompatibilityBootstrap.ResetTestState();

        using var factory = new CoveWebApplicationFactory("IntegrationStartup");
        using var client = factory.CreateAuthenticatedClient();

        for (var attempt = 0; attempt < 50 && SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaInvocationCount == 0; attempt++)
            await Task.Delay(100);

        Assert.True(SchemaCompatibilityBootstrap.EnsureCompatibilitySchemaInvocationCount > 0);

        await factory.WithDbContextAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = CoveWebApplicationFactory.TestUserId,
                Username = "integration-user",
                PasswordHash = "integration-test",
                PasswordAlgo = "integration-test",
                IsActive = true,
                IsSystem = true,
            });
            await db.SaveChangesAsync();
        });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}