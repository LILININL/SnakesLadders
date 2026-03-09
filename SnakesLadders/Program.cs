using SnakesLadders.Hubs;
using SnakesLadders.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
int? httpsPort = null;

if (!string.IsNullOrWhiteSpace(urls))
{
    foreach (var rawUrl in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            httpsPort = uri.Port;
            break;
        }
    }
}

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
if (httpsPort.HasValue)
{
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort.Value);
}

builder.Services.AddSingleton<IBoardGenerator, BoardGenerator>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<IGameRoomModule, SnakesLaddersGameRoomModule>();
builder.Services.AddSingleton<IGameRoomModule, MonopolyGameRoomModule>();
builder.Services.AddSingleton<IGameRoomService, GameRoomService>();
builder.Services.AddHostedService<TurnTimerBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials();
    });
});

var app = builder.Build();
var staticContentTypeProvider = new FileExtensionContentTypeProvider();
staticContentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
staticContentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
staticContentTypeProvider.Mappings[".bin"] = "application/octet-stream";
staticContentTypeProvider.Mappings[".usdz"] = "model/vnd.usdz+zip";
staticContentTypeProvider.Mappings[".obj"] = "text/plain";
staticContentTypeProvider.Mappings[".mtl"] = "text/plain";

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (httpsPort.HasValue || !app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("DevCors");
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypeProvider,
    OnPrepareResponse = context =>
    {
        var extension = Path.GetExtension(context.File.Name);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    Status = "ok",
    Utc = DateTimeOffset.UtcNow
}));

app.MapGet("/rooms/waiting", (IGameRoomService roomService) =>
    Results.Ok(roomService.GetPublicRooms()));

app.MapGet("/games", (IGameRoomService roomService) =>
    Results.Ok(roomService.GetAvailableGames()));

app.MapGet("/lobby/online", (IGameRoomService roomService) =>
    Results.Ok(roomService.GetLobbyOnlineUsers()));

app.MapHub<GameHub>("/hubs/game");

app.Run();
