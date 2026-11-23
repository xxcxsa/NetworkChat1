namespace NetworkChat.Shared.Models;

public sealed class ChatMessage
{
    public string UserName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.Now;
}
