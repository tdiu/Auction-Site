using System.Collections.Concurrent;

namespace API.SignalR;

public class PresenceTracker
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> OnlineUsers = new();

    public Task<bool> UserConnected(string userId, string connectionId)
    {
        var connections = OnlineUsers.GetOrAdd(userId, _ =>
            new ConcurrentDictionary<string, byte>());

        var isFirst = connections.IsEmpty;
        connections.TryAdd(connectionId, 0);
        return Task.FromResult(isFirst);
    }

    public Task<bool> UserDisconnected(string userId, string connectionId)
    {
        if (OnlineUsers.TryGetValue(userId, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                OnlineUsers.TryRemove(userId, out _);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<string[]> GetOnlineUsers() => Task.FromResult(OnlineUsers.Keys.OrderBy(x => x).ToArray());
}
