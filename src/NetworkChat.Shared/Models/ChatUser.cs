namespace NetworkChat.Shared.Models;

public sealed class ChatUser
{
    public string Name { get; set; } = "";
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.Now;
}
