using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Cove.Core.Entities;
using Cove.Plugins;

namespace Cove.Data.Repositories;

/// <summary>
/// EF Core implementation of IExtensionStore, scoped to a single extension ID.
/// </summary>
public class EfExtensionStore : IExtensionStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _extensionId;

    public EfExtensionStore(IServiceScopeFactory scopeFactory, string extensionId)
    {
        _scopeFactory = scopeFactory;
        _extensionId = extensionId;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var entry = await db.ExtensionData
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExtensionId == _extensionId && e.Key == key, ct);
        return entry?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var entry = await db.ExtensionData
            .FirstOrDefaultAsync(e => e.ExtensionId == _extensionId && e.Key == key, ct);

        if (entry is not null)
        {
            entry.Value = value;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.ExtensionData.Add(new ExtensionData
            {
                ExtensionId = _extensionId,
                Key = key,
                Value = value
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        var entry = await db.ExtensionData
            .FirstOrDefaultAsync(e => e.ExtensionId == _extensionId && e.Key == key, ct);
        if (entry is not null)
        {
            db.ExtensionData.Remove(entry);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
        return await db.ExtensionData
            .AsNoTracking()
            .Where(e => e.ExtensionId == _extensionId)
            .ToDictionaryAsync(e => e.Key, e => e.Value, ct);
    }
}

/// <summary>
/// Factory that creates scoped IExtensionStore instances for each extension.
/// </summary>
public class EfExtensionStoreFactory : IExtensionStoreFactory
{
    private readonly IServiceProvider _services;

    public EfExtensionStoreFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IExtensionStore CreateStore(string extensionId)
    {
        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        return new EfExtensionStore(scopeFactory, extensionId);
    }
}
