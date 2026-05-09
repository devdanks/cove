using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using Cove.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests.Integration;

public sealed class DatabaseUnavailableMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_MapsTransientDatabaseConnectionFailure_ToServiceUnavailable()
    {
        var failure = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new ConnectionRefusedDbException());
        var middleware = new DatabaseUnavailableMiddleware(_ => Task.FromException(failure));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, NullLogger<DatabaseUnavailableMiddleware>.Instance);

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("DATABASE_UNAVAILABLE", body);
    }

    [Fact]
    public void IsTransientDatabaseConnectionFailure_DoesNotClassifyPlainSocketFailure()
    {
        var exception = new SocketException((int)SocketError.ConnectionRefused);

        Assert.False(DatabaseUnavailableExceptionClassifier.IsTransientDatabaseConnectionFailure(exception));
    }

    private sealed class ConnectionRefusedDbException : DbException
    {
        public ConnectionRefusedDbException()
            : base("Database connection refused.", new SocketException((int)SocketError.ConnectionRefused))
        {
        }
    }
}