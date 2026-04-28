using Microsoft.AspNetCore.SignalR;
using System.Threading;

namespace Cove.Api.Hubs;

public class JobHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("ConnectionEstablished", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}

public class LogHub : Hub
{
    private static int _connectionCount;

    public static bool HasActiveConnections => Volatile.Read(ref _connectionCount) > 0;

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectionCount);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectionCount);
        await base.OnDisconnectedAsync(exception);
    }
}
