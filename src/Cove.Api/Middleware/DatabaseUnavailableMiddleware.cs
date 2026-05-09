using System.Data.Common;
using System.Net.Sockets;
using Npgsql;

namespace Cove.Api.Middleware;

public sealed class DatabaseUnavailableMiddleware
{
    public const int RetryAfterSeconds = 5;
    private readonly RequestDelegate _next;

    public DatabaseUnavailableMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILogger<DatabaseUnavailableMiddleware> logger)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (DatabaseUnavailableExceptionClassifier.IsTransientDatabaseConnectionFailure(ex))
        {
            if (context.Response.HasStarted)
                throw;

            var reason = ex.GetBaseException().Message;
            logger.LogWarning("Database temporarily unavailable while handling {Method} {Path}: {Reason}",
                context.Request.Method,
                context.Request.Path.Value,
                reason);
            logger.LogDebug(ex, "Transient database connection failure handled at request boundary.");

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(CreateResponse(), context.RequestAborted);
        }
    }

    public static object CreateResponse() => new
    {
        code = "DATABASE_UNAVAILABLE",
        message = "The database is temporarily unavailable. Try again in a few seconds.",
    };
}

public static class DatabaseUnavailableExceptionClassifier
{
    public static bool IsTransientDatabaseConnectionFailure(Exception exception)
    {
        var sawDatabaseException = false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException npgsqlException)
            {
                if (npgsqlException.IsTransient)
                    return true;

                sawDatabaseException = true;
            }
            else if (current is DbException)
            {
                sawDatabaseException = true;
            }

            if (sawDatabaseException && current is SocketException socketException && IsTransientSocketError(socketException.SocketErrorCode))
                return true;

            if (sawDatabaseException && current is TimeoutException)
                return true;

            if (sawDatabaseException && current is EndOfStreamException)
                return true;
        }

        return false;
    }

    private static bool IsTransientSocketError(SocketError socketError) => socketError switch
    {
        SocketError.ConnectionAborted
            or SocketError.ConnectionRefused
            or SocketError.ConnectionReset
            or SocketError.HostDown
            or SocketError.HostNotFound
            or SocketError.NetworkDown
            or SocketError.NetworkUnreachable
            or SocketError.TimedOut
            or SocketError.TryAgain => true,
        _ => false,
    };
}