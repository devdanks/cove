using Cove.Api.Services;
using Cove.Core.Auth;
using Cove.Core.Interfaces;

namespace Cove.Api.Middleware;

public sealed class OutsideIpFailsafeMiddleware
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private readonly RequestDelegate _next;

    public OutsideIpFailsafeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        CoveConfiguration config,
        ConfigService configService,
        IUserService users,
        IAuditService audit,
        ILogger<OutsideIpFailsafeMiddleware> logger)
    {
        var remoteAddress = AuthDisabledRequestGuard.GetEffectiveRemoteAddress(context, config.Auth);
        if (config.Auth.Enabled || AuthDisabledRequestGuard.IsTrustedLocalRequest(context, config.Auth))
        {
            await _next(context);
            return;
        }

        await Lock.WaitAsync(context.RequestAborted);
        try
        {
            if (!config.Auth.Enabled)
            {
                config.Auth.Enabled = true;
                try
                {
                    await configService.SaveCurrentConfigAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist auth failsafe enablement after request from {RemoteIp}", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
            }
        }
        finally
        {
            Lock.Release();
        }

        var ip = remoteAddress?.ToString();
        var ua = context.Request.Headers.UserAgent.ToString();
        var ownerExists = false;
        try
        {
            ownerExists = await users.OwnerExistsAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not determine owner account status during auth failsafe.");
        }

        SetupTokenDto? setupToken = null;
        if (!ownerExists)
        {
            try
            {
                setupToken = await users.CreateSetupTokenAsync(CovePrincipal.Anonymous(ip, ua), context.RequestAborted);
                await WriteSetupTokenFileAsync(setupToken, context.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create auth setup token during auth failsafe.");
            }
        }

        await audit.LogAsync(
            AuditActions.AuthFailsafeEnabled,
            AuditOutcomes.Deny,
            CovePrincipal.Anonymous(ip, ua),
            "auth",
            "enabled",
            new
            {
                method = context.Request.Method,
                path = context.Request.Path.Value,
                remoteIp = ip,
                setupTokenRequired = !ownerExists,
                setupTokenExpiresAt = setupToken?.ExpiresAt,
            },
            context.RequestAborted);

        if (!ownerExists)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "AuthFailsafeEnabled",
                setupTokenRequired = true,
            }, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "AUTH_LOCKDOWN_TRIGGERED",
            message = "Authentication was automatically enabled after a public remote request was detected while authentication was disabled.",
        }, context.RequestAborted);
    }

    private static async Task WriteSetupTokenFileAsync(SetupTokenDto token, CancellationToken ct)
    {
        var dataDir = CoveDefaultPaths.GetDataRoot();
        Directory.CreateDirectory(dataDir);
        var tokenPath = Path.Combine(dataDir, "setup_token.txt");
        await File.WriteAllTextAsync(tokenPath,
            $"Cove enabled authentication after a public remote request.\n" +
            $"Use this one-time setup token at /auth/redeem-invite or with /api/auth/setup-token-redeem.\n" +
            $"Token: {token.Token}\n" +
            $"Expires: {token.ExpiresAt:o}\n",
            ct);
    }
}