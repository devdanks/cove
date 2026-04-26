using Cove.Core.Auth;

namespace Cove.Api.Middleware;

/// <summary>
/// Resolves the bearer/API token from the incoming request and populates
/// <see cref="ICurrentPrincipalAccessor"/> for the duration of the request.
/// </summary>
public sealed class CurrentPrincipalMiddleware
{
    private readonly RequestDelegate _next;

    public CurrentPrincipalMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITokenService tokens, ICurrentPrincipalAccessor accessor)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var ua = context.Request.Headers.UserAgent.ToString();

        // Allow SignalR / file-stream endpoints to pass the token via ?access_token=
        string? authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader))
        {
            var qsToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(qsToken))
                authHeader = "Bearer " + qsToken;
        }

        var principal = await tokens.ResolveAsync(authHeader, ip, ua, context.RequestAborted);
        if (principal is not null)
        {
            accessor.Set(principal);
        }
        else
        {
            accessor.Set(CovePrincipal.Anonymous(ip, ua));
        }
        await _next(context);
    }
}
