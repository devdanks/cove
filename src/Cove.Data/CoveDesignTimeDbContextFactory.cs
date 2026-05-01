using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cove.Data;

public sealed class CoveDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CoveContext>
{
    public CoveContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CoveContext>();
        var connectionString = Environment.GetEnvironmentVariable("COVE_CONNECTION_STRING")
            ?? "Host=127.0.0.1;Port=5432;Database=cove_design;Username=postgres;Password=postgres;Trust Server Certificate=true";

        optionsBuilder.UseNpgsql(connectionString);
        return new CoveContext(optionsBuilder.Options);
    }
}