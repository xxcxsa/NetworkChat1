namespace NetworkChat.Shared.Models;

public sealed class UdpNotification
{
    public string Type { get; set; } = "";
    public string? UserName { get; set; }
    public int OnlineCount { get; set; }
    public IReadOnlyList<string> Users { get; set; } = Array.Empty<string>();
}
