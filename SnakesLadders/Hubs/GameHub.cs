using Microsoft.AspNetCore.SignalR;
using SnakesLadders.Contracts;
using SnakesLadders.Services;

namespace SnakesLadders.Hubs;

public sealed class GameHub : Hub
{
    private readonly IGameRoomService _roomService;

    public GameHub(IGameRoomService roomService)
    {
        _roomService = roomService;
    }

    public async Task CreateRoom(CreateRoomRequest request)
    {
        var result = _roomService.CreateRoom(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to create room.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Value.RoomCode);
        await Clients.Caller.SendAsync("RoomCreated", result.Value);
        await Clients.Group(result.Value.RoomCode).SendAsync("RoomUpdated", result.Value.Room);
    }

    public async Task JoinRoom(JoinRoomRequest request)
    {
        var result = _roomService.JoinRoom(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to join room.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Value.RoomCode);
        await Clients.Caller.SendAsync("RoomJoined", result.Value);
        await Clients.Group(result.Value.RoomCode).SendAsync("RoomUpdated", result.Value.Room);
    }

    public async Task ResumeRoom(ResumeRoomRequest request)
    {
        var result = _roomService.ResumeRoom(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to resume room.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, result.Value.RoomCode);
        await Clients.Caller.SendAsync("RoomResumed", result.Value);
        await Clients.Group(result.Value.RoomCode).SendAsync("RoomUpdated", result.Value.Room);
    }

    public async Task StartGame(StartGameRequest request)
    {
        var result = _roomService.StartGame(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to start game.");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("GameStarted", result.Value);
        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task RollDice(RollDiceRequest request)
    {
        var result = _roomService.RollDice(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to roll dice.");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("DiceRolled", result.Value);
        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value.Room);

        if (result.Value.Turn.IsGameFinished)
        {
            await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("GameFinished", result.Value);

            var resetResult = _roomService.ResetFinishedGame(request.RoomCode);
            if (resetResult.Success && resetResult.Value is not null)
            {
                await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", resetResult.Value);
            }
        }
        else
        {
            await Clients.Group(request.RoomCode.ToUpperInvariant())
                .SendAsync("TurnChanged", result.Value.Room.CurrentTurnPlayerId);
        }
    }

    public async Task SetReady(SetReadyRequest request)
    {
        var result = _roomService.SetReady(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to set ready state.");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task SendChat(SendChatRequest request)
    {
        var result = _roomService.SendChat(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to send chat.");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("ChatReceived", result.Value);
    }

    public async Task LeaveRoom(LeaveRoomRequest request)
    {
        var result = _roomService.LeaveRoom(Context.ConnectionId, request.RoomCode);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Failed to leave room.");
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, request.RoomCode.ToUpperInvariant());
        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task GetRoom(string roomCode)
    {
        var result = _roomService.GetRoom(roomCode);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "Room not found.");
            return;
        }

        await Clients.Caller.SendAsync("RoomUpdated", result.Value);
    }

    public Task SetLobbyName(string displayName)
    {
        _roomService.UpsertLobbyPresence(Context.ConnectionId, displayName);
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = _roomService.HandleDisconnect(Context.ConnectionId);
        if (result.Success && result.Value is not null)
        {
            await Clients.Group(result.Value.RoomCode).SendAsync("RoomUpdated", result.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task SendError(string message) =>
        Clients.Caller.SendAsync("Error", message);
}
