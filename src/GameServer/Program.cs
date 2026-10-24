using Jentl.Net;
using GameServer;
using Uno;

// The Jentl game server: players connect to /play with a WebSocket, then create or join rooms.
// Listens on http://0.0.0.0:5080 unless ASPNETCORE_URLS (or --urls) says otherwise.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5080");
builder.Services.AddSingleton(_ => new RoomServer(new Dictionary<string, Func<IRoomGame>>
{
    [UnoOnline.Game] = () => new UnoRoomGame(),
}));
builder.Services.AddHostedService<RoomTicker>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/", (RoomServer rooms) => $"Jentl game server: {rooms.RoomCount} rooms open.");
app.Map("/play", async (HttpContext context, RoomServer rooms) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect with a WebSocket.").ConfigureAwait(false);
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    using var connection = new SocketConnection(socket, rooms);
    await connection.RunAsync(context.RequestAborted).ConfigureAwait(false);
});

await app.RunAsync().ConfigureAwait(false);

/// <summary>The entry point, visible to the integration tests.</summary>
public partial class Program;
