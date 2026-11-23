namespace NetworkChat.Shared.Models;

public sealed class ServerPacket
{
    public string Type { get; set; } = "message";
    public string? UserName { get; set; }
    public string? Text { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int? OnlineCount { get; set; }
}
