using SnakesLadders.Domain;

namespace SnakesLadders.Contracts;

public sealed class CreateRoomRequest
{
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; } = 1;
    public string? GameKey { get; set; }
    public BoardOptions? BoardOptions { get; set; }
}

public sealed class JoinRoomRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; } = 1;
    public string? SessionId { get; set; }
}

public sealed class ResumeRoomRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int AvatarId { get; set; } = 1;
}

public sealed class StartGameRequest
{
    public string RoomCode { get; set; } = string.Empty;
}

public sealed class RollDiceRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public bool UseLuckyReroll { get; set; }
    public ForkPathChoice? ForkChoice { get; set; }
}

public sealed class SetReadyRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public bool IsReady { get; set; }
}

public sealed class SetAvatarRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public int AvatarId { get; set; } = 1;
}

public sealed class LeaveRoomRequest
{
    public string RoomCode { get; set; } = string.Empty;
}

public sealed class SendChatRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class CreateRoomResponse
{
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required string SessionId { get; init; }
    public required RoomSnapshot Room { get; init; }
}

public sealed class JoinRoomResponse
{
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required string SessionId { get; init; }
    public required RoomSnapshot Room { get; init; }
}

public sealed class ResumeRoomResponse
{
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required string SessionId { get; init; }
    public required RoomSnapshot Room { get; init; }
}

public sealed class TurnEnvelope
{
    public required TurnResult Turn { get; init; }
    public required RoomSnapshot Room { get; init; }
}

public sealed class ChatMessage
{
    public required string MessageId { get; init; }
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }
}

public sealed class RoomSnapshot
{
    public required string RoomCode { get; init; }
    public required string GameKey { get; init; }
    public required string HostPlayerId { get; init; }
    public required GameStatus Status { get; init; }
    public required BoardOptions BoardOptions { get; init; }
    public required IReadOnlyList<PlayerSnapshot> Players { get; init; }
    public required string? CurrentTurnPlayerId { get; init; }
    public required int TurnCounter { get; init; }
    public required int CompletedRounds { get; init; }
    public required DateTimeOffset? TurnDeadlineUtc { get; init; }
    public required string? WinnerPlayerId { get; init; }
    public required string? FinishReason { get; init; }
    public required BoardSnapshot? Board { get; init; }
}

public sealed class BoardSnapshot
{
    public required int Size { get; init; }
    public required IReadOnlyList<Jump> Jumps { get; init; }
    public required IReadOnlyList<ForkCell> ForkCells { get; init; }
    public required Jump? ActiveFrenzySnake { get; init; }
    public required IReadOnlyList<Jump> TemporaryJumps { get; init; }
    public required IReadOnlyList<BoardItem> Items { get; init; }
    public required IReadOnlyList<int> BananaTrapCells { get; init; }
    public IReadOnlyList<MonopolyCellSnapshot>? MonopolyCells { get; init; }
    public int MonopolyFreeParkingPot { get; init; }
}

public sealed class PlayerSnapshot
{
    public required string PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required int AvatarId { get; init; }
    public required int Position { get; init; }
    public required bool Connected { get; init; }
    public required bool IsReady { get; init; }
    public required int Shields { get; init; }
    public required int LuckyRerollsLeft { get; init; }
    public required int SnakeRepellentCharges { get; init; }
    public required bool LadderHackPending { get; init; }
    public required bool AnchorActive { get; init; }
    public required int AnchorTurnsLeft { get; init; }
    public int Cash { get; init; }
    public bool IsBankrupt { get; init; }
    public int JailTurnsRemaining { get; init; }
}

public sealed class MonopolyCellSnapshot
{
    public required int Cell { get; init; }
    public required string Name { get; init; }
    public required MonopolyCellType Type { get; init; }
    public string? ColorGroup { get; init; }
    public int Price { get; init; }
    public int Rent { get; init; }
    public int Fee { get; init; }
    public string? OwnerPlayerId { get; init; }
}

public sealed class PublicRoomSummary
{
    public required string RoomCode { get; init; }
    public required string GameKey { get; init; }
    public required GameStatus Status { get; init; }
    public required string HostName { get; init; }
    public required int PlayerCount { get; init; }
    public required int BoardSize { get; init; }
    public required DensityMode DensityMode { get; init; }
}

public sealed class PublicGameSummary
{
    public required string GameKey { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required bool IsAvailable { get; init; }
}

public sealed class LobbyOnlineUser
{
    public required string DisplayName { get; init; }
    public required string ConnectionId { get; init; }
    public required string? RoomCode { get; init; }
}
