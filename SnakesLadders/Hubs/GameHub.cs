using Microsoft.AspNetCore.SignalR;
using SnakesLadders.Contracts;
using SnakesLadders.Domain;
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
            await SendError(result.Error ?? "สร้างห้องไม่สำเร็จ");
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
            await SendError(result.Error ?? "เข้าห้องไม่สำเร็จ");
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
            await SendError(result.Error ?? "กลับเข้าห้องเดิมไม่สำเร็จ");
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
            await SendError(result.Error ?? "เริ่มเกมไม่สำเร็จ");
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
            await SendError(result.Error ?? "ทอยเต๋าไม่สำเร็จ");
            return;
        }

        var roomCode = request.RoomCode.ToUpperInvariant();
        await Clients.Group(roomCode).SendAsync("GameActionApplied", result.Value);

        if (result.Value.Room.GameKey.Equals(GameCatalog.SnakesLadders, StringComparison.OrdinalIgnoreCase))
        {
            await Clients.Group(roomCode).SendAsync("DiceRolled", result.Value);
        }

        await Clients.Group(roomCode).SendAsync("RoomUpdated", result.Value.Room);

        if (result.Value.Turn.IsGameFinished)
        {
            await Clients.Group(roomCode).SendAsync("GameFinished", result.Value);

            var resetResult = _roomService.ResetFinishedGame(request.RoomCode);
            if (resetResult.Success && resetResult.Value is not null)
            {
                await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", resetResult.Value);
            }
        }
        else
        {
            await Clients.Group(roomCode)
                .SendAsync("TurnChanged", result.Value.Room.CurrentTurnPlayerId);
        }
    }

    public async Task SubmitGameAction(SubmitGameActionRequest request)
    {
        var result = _roomService.SubmitGameAction(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "ดำเนินการไม่สำเร็จ");
            return;
        }

        var roomCode = request.RoomCode.ToUpperInvariant();
        await Clients.Group(roomCode).SendAsync("GameActionApplied", result.Value);

        if (result.Value.Turn.ActionType == GameActionType.RollDice &&
            result.Value.Room.GameKey.Equals(GameCatalog.SnakesLadders, StringComparison.OrdinalIgnoreCase))
        {
            await Clients.Group(roomCode).SendAsync("DiceRolled", result.Value);
        }

        await Clients.Group(roomCode).SendAsync("RoomUpdated", result.Value.Room);

        if (result.Value.Turn.IsGameFinished)
        {
            await Clients.Group(roomCode).SendAsync("GameFinished", result.Value);

            var resetResult = _roomService.ResetFinishedGame(request.RoomCode);
            if (resetResult.Success && resetResult.Value is not null)
            {
                await Clients.Group(roomCode).SendAsync("RoomUpdated", resetResult.Value);
            }
        }
        else
        {
            await Clients.Group(roomCode)
                .SendAsync("TurnChanged", result.Value.Room.CurrentTurnPlayerId);
        }
    }

    public async Task SetReady(SetReadyRequest request)
    {
        var result = _roomService.SetReady(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "เปลี่ยนสถานะความพร้อมไม่สำเร็จ");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task AddBotPlayer(AddBotPlayerRequest request)
    {
        var result = _roomService.AddBotPlayer(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "เพิ่ม AI ไม่สำเร็จ");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task RemoveBotPlayer(RemoveBotPlayerRequest request)
    {
        var result = _roomService.RemoveBotPlayer(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "นำ AI ออกจากห้องไม่สำเร็จ");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task SetFullAuto(SetFullAutoRequest request)
    {
        var result = _roomService.SetFullAuto(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "เปลี่ยนสถานะ Full Auto ไม่สำเร็จ");
            return;
        }

        await Clients.Caller.SendAsync("FullAutoUpdated", result.Value);
        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task SetAvatar(SetAvatarRequest request)
    {
        var result = _roomService.SetAvatar(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "เปลี่ยน Avatar ไม่สำเร็จ");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("RoomUpdated", result.Value);
    }

    public async Task SendChat(SendChatRequest request)
    {
        var result = _roomService.SendChat(Context.ConnectionId, request);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "ส่งข้อความไม่สำเร็จ");
            return;
        }

        await Clients.Group(request.RoomCode.ToUpperInvariant()).SendAsync("ChatReceived", result.Value);
    }

    public async Task LeaveRoom(LeaveRoomRequest request)
    {
        var result = _roomService.LeaveRoom(Context.ConnectionId, request.RoomCode);
        if (!result.Success || result.Value is null)
        {
            await SendError(result.Error ?? "ออกห้องไม่สำเร็จ");
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
            await SendError(result.Error ?? "ไม่พบห้องที่ต้องการ");
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
