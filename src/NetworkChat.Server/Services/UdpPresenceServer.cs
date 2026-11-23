using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetworkChat.Shared.Models;

namespace NetworkChat.Server.Services;

public sealed class UdpPresenceServer(
    ChatState state,
    IConfiguration configuration,
    ILogger<UdpPresenceServer> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, IPEndPoint> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    private UdpClient? _udp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = configuration.GetValue("Chat:UdpPort", 5001);
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        state.UserDisconnected += OnUserDisconnected;

        logger.LogInformation("UDP presence server listening on {Port}", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(stoppingToken);
                var message = Encoding.UTF8.GetString(result.Buffer);
                await ProcessMessageAsync(message, result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            state.UserDisconnected -= OnUserDisconnected;
            _udp.Dispose();
        }
    }

    private async Task ProcessMessageAsync(string message, IPEndPoint remoteEndPoint)
    {
        var separator = message.IndexOf('|');
        if (separator < 0) return;

        var command = message[..separator];
        var name = message[(separator + 1)..].Trim();
        if (name.Length is 0 or > 24) return;

        if (command == "JOIN")
        {
            if (!state.GetUserNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                return;

            var isNewRegistration =
                !_clients.TryGetValue(name, out var previousEndpoint) ||
                !previousEndpoint.Equals(remoteEndPoint);

            _clients[name] = remoteEndPoint;
            var users = state.GetUserNames();
            await SendAsync(remoteEndPoint, new UdpNotification
            {
                Type = "SNAPSHOT",
                OnlineCount = users.Count,
                Users = users
            });
            if (isNewRegistration)
            {
                await BroadcastAsync(new UdpNotification
                {
                    Type = "ONLINE",
                    UserName = name,
                    OnlineCount = users.Count
                }, except: name);
            }

            logger.LogInformation("UDP presence registered: {Name} ({Endpoint})", name, remoteEndPoint);
        }
    }

    private void OnUserDisconnected(string name)
    {
        _clients.TryRemove(name, out _);
        _ = BroadcastAsync(new UdpNotification
        {
            Type = "OFFLINE",
            UserName = name,
            OnlineCount = state.GetUserNames().Count
        });
    }

    private async Task BroadcastAsync(UdpNotification notification, string? except = null)
    {
        foreach (var pair in _clients)
        {
            if (string.Equals(pair.Key, except, StringComparison.OrdinalIgnoreCase))
                continue;

            try { await SendAsync(pair.Value, notification); }
            catch (SocketException ex)
            {
                logger.LogDebug(ex, "Could not send UDP presence to {Name}", pair.Key);
            }
        }
    }

    private async Task SendAsync(IPEndPoint endpoint, UdpNotification notification)
    {
        if (_udp is null) return;
        var json = JsonSerializer.Serialize(notification);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _udp.SendAsync(bytes, endpoint);
    }
}
