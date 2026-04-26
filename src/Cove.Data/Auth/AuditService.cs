using System.Text.Json;
using System.Threading.Channels;
using Cove.Core.Auth;
using Cove.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cove.Data.Auth;

/// <summary>
/// Background-flushed audit log writer. <see cref="LogAsync"/> never throws and never blocks.
/// </summary>
public sealed class AuditService : BackgroundService, IAuditService
{
    private readonly Channel<AuditEvent> _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(8192)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    });
    private readonly IServiceProvider _services;
    private readonly ILogger<AuditService> _log;

    public AuditService(IServiceProvider services, ILogger<AuditService> log)
    {
        _services = services;
        _log = log;
    }

    public Task LogAsync(string action, string outcome, CovePrincipal? actor = null,
        string? targetKind = null, string? targetId = null, object? detail = null,
        CancellationToken ct = default)
    {
        try
        {
            var ev = new AuditEvent
            {
                OccurredAt = DateTime.UtcNow,
                Action = action,
                Outcome = outcome,
                ActorUserId = actor?.UserId,
                ActorKind = actor?.Kind switch
                {
                    PrincipalKind.User => "user",
                    PrincipalKind.ApiToken => "api_token",
                    PrincipalKind.ShareLink => "share_link",
                    PrincipalKind.System => "system",
                    PrincipalKind.Anonymous => "anonymous",
                    _ => "system",
                },
                Ip = actor?.Ip,
                UserAgent = actor?.UserAgent,
                TargetKind = targetKind,
                TargetId = targetId,
                Detail = detail is null ? null : JsonSerializer.Serialize(detail),
            };
            _channel.Writer.TryWrite(ev);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Audit log enqueue failed (suppressed)");
        }
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditEvent>(64);
        try
        {
            await foreach (var ev in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(ev);
                // drain quickly
                while (batch.Count < 64 && _channel.Reader.TryRead(out var more))
                    batch.Add(more);
                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
                    db.AuditEvents.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Audit batch flush failed (suppressed)");
                }
                batch.Clear();
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }
}
