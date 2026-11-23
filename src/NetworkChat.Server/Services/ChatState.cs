using System.Collections.Concurrent;
using System.Net.Sockets;
using NetworkChat.Shared.Models;

namespace NetworkChat.Server.Services;

public sealed class ChatState
{
    private readonly ConcurrentDictionary<string, ClientConnection> _clients =
        new(StringComparer.OrdinalIgnoreCase);
    private long _totalMessages;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public event Action<string>? UserDisconnected;

    public IReadOnlyList<ChatUser> GetUsers() => _clients.Values
        .Select(c => new ChatUser { Name = c.Name, ConnectedAt = c.ConnectedAt })
        .OrderBy(u => u.Name).ToList();

    public IReadOnlyList<string> GetUserNames() =>
        _clients.Keys.OrderBy(x => x).ToArray();

    public ChatStatistics GetStatistics() => new()
    {
        OnlineUsers = _clients.Count,
        TotalMessages = Interlocked.Read(ref _totalMessages),
        StartedAt = StartedAt,
        Users = GetUserNames()
    };

    public bool TryAdd(ClientConnection client) => _clients.TryAdd(client.Name, client);

    public bool Remove(string name, out ClientConnection client)
    {
        var removed = _clients.TryRemove(name, out client!);
        if (removed) UserDisconnected?.Invoke(name);
        return removed;
    }

    public void CountMessage() => Interlocked.Increment(ref _totalMessages);

    public async Task BroadcastAsync(ServerPacket packet, string? except = null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(packet);
        var dead = new List<string>();

        foreach (var pair in _clients)
        {
            if (string.Equals(pair.Key, except, StringComparison.OrdinalIgnoreCase))
                continue;

            try { await pair.Value.SendAsync(json); }
            catch { dead.Add(pair.Key); }
        }

        foreach (var name in dead)
        {
            if (Remove(name, out var client))
                await client.DisposeAsync();
        }
    }

    public async Task<bool> RemoveUserAsync(string name)
    {
        if (!Remove(name, out var client)) return false;
        await client.DisposeAsync();
        await BroadcastAsync(new ServerPacket
            { Type = "users", OnlineCount = _clients.Count });
        return true;
    }
}

public sealed class ClientConnection(string name, StreamWriter writer, TcpClient tcpClient)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Name { get; } = name;
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.Now;

    public async Task SendAsync(string text)
    {
        await _writeLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(text);
            await writer.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await writer.DisposeAsync(); } catch { }
        tcpClient.Dispose();
        _writeLock.Dispose();
    }
}
