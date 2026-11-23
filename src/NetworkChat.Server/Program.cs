using NetworkChat.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ChatState>();
builder.Services.AddHostedService<TcpChatServer>();
builder.Services.AddHostedService<UdpPresenceServer>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "NetworkChat.Server",
    status = "running",
    tcpPort = app.Configuration.GetValue("Chat:TcpPort", 5000),
    udpPort = app.Configuration.GetValue("Chat:UdpPort", 5001)
}));

app.MapGet("/api/users", (ChatState state) =>
    Results.Ok(state.GetUsers()));

app.MapGet("/api/stats", (ChatState state) =>
    Results.Ok(state.GetStatistics()));

app.MapDelete("/api/admin/users/{name}", async (string name, ChatState state) =>
{
    var removed = await state.RemoveUserAsync(name);
    return removed ? Results.Ok(new { message = $"User {name} disconnected." })
                   : Results.NotFound(new { error = "User not found." });
});

app.Run("http://0.0.0.0:5080");
