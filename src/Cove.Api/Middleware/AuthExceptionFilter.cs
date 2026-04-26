using Cove.Core.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Cove.Api.Middleware;

/// <summary>
/// Maps <see cref="ForbiddenException"/> / <see cref="UnauthorizedException"/> thrown
/// from service methods into clean 403 / 401 responses.
/// </summary>
public sealed class AuthExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ForbiddenException fe:
                context.Result = new ObjectResult(new
                {
                    code = "FORBIDDEN",
                    message = fe.Message,
                    missing = fe.MissingPermission is null ? null : new[] { fe.MissingPermission },
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                context.ExceptionHandled = true;
                break;
            case UnauthorizedException ue:
                context.Result = new ObjectResult(new { code = "UNAUTHORIZED", message = ue.Message })
                { StatusCode = StatusCodes.Status401Unauthorized };
                context.ExceptionHandled = true;
                break;
        }
    }
}
