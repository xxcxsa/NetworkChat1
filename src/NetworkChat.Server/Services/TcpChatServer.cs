using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetworkChat.Shared.Models;

namespace NetworkChat.Server.Services;

public sealed class TcpChatServer(
    ChatState state,
    IConfiguration configuration,
    ILogger<TcpChatServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = configuration.GetValue("Chat:TcpPort", 5000);
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        logger.LogInformation("TCP chat server listening on {Port}", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var tcp = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(tcp, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken token)
    {
        string? userName = null;
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };

        try
        {
            var hello = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(hello)) return;

            userName = hello.Trim();
            if (userName.Length > 24 || userName.Any(char.IsControl))
            {
                await writer.WriteLineAsync("{\"type\":\"error\",\"text\":\"Invalid user name\"}");
                return;
            }

            var connection = new ClientConnection(userName, writer, tcp);
            if (!state.TryAdd(connection))
            {
                await writer.WriteLineAsync("{\"type\":\"error\",\"text\":\"Name is already in use\"}");
                return;
            }

            await connection.SendAsync(JsonSerializer.Serialize(new ServerPacket
                { Type = "system", Text = $"Connected as {userName}" }));

            await state.BroadcastAsync(new ServerPacket
                { Type = "users", OnlineCount = state.GetUsers().Count });

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line is null) break;

                var text = line.Trim();
                if (text.Length == 0) continue;
                if (text.Length > 2000) text = text[..2000];

                state.CountMessage();
                await state.BroadcastAsync(new ServerPacket
                {
                    Type = "message",
                    UserName = userName,
                    Text = text,
                    SentAt = DateTimeOffset.Now
                }, userName);
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogWarning(ex, "Client connection failed"); }
        finally
        {
            if (userName is not null && state.Remove(userName, out var removed))
            {
                await removed.DisposeAsync();
                await state.BroadcastAsync(new ServerPacket
                    { Type = "users", OnlineCount = state.GetUsers().Count });
            }
            else
            {
                try { tcp.Dispose(); } catch { }
            }
        }
    }
}
