namespace NetworkChat.Shared.Models;

public sealed class ChatStatistics
{
    public int OnlineUsers { get; set; }
    public long TotalMessages { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public IReadOnlyList<string> Users { get; set; } = Array.Empty<string>();
}
