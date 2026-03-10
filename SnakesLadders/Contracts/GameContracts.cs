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

public sealed class SubmitGameActionRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public GameActionType ActionType { get; set; } = GameActionType.RollDice;
    public bool UseLuckyReroll { get; set; }
    public ForkPathChoice? ForkChoice { get; set; }
    public MonopolyActionPayload? Monopoly { get; set; }
}

public enum GameActionType
{
    RollDice = 0,
    PayJailFine = 1,
    TryJailRoll = 2,
    BuyProperty = 3,
    DeclinePurchase = 4,
    BidAuction = 5,
    PassAuction = 6,
    BuildHouse = 7,
    SellHouse = 8,
    Mortgage = 9,
    Unmortgage = 10,
    OfferTrade = 11,
    AcceptTrade = 12,
    RejectTrade = 13,
    DeclareBankruptcy = 14,
    EndTurn = 15,
    SellProperty = 16
}

public sealed class MonopolyActionPayload
{
    public int? CellId { get; set; }
    public int? BidAmount { get; set; }
    public string? TargetPlayerId { get; set; }
    public MonopolyTradeOfferPayload? TradeOffer { get; set; }
}

public sealed class MonopolyTradeOfferPayload
{
    public int CashGive { get; set; }
    public int CashReceive { get; set; }
    public IReadOnlyList<int> GiveCells { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> ReceiveCells { get; set; } = Array.Empty<int>();
}

public sealed class SetReadyRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public bool IsReady { get; set; }
}

public sealed class AddBotPlayerRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public BotDifficulty Difficulty { get; set; } = BotDifficulty.Aggressive;
    public BotPersonality Personality { get; set; } = BotPersonality.Adaptive;
}

public sealed class RemoveBotPlayerRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
}

public sealed class SetFullAutoRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class VoteFinalDuelRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public bool Support { get; set; } = true;
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
    public required long SnapshotRevision { get; init; }
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
    public MonopolyStateSnapshot? MonopolyState { get; init; }
    public GameResultSnapshot? GameResult { get; init; }
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
    public required bool IsBot { get; init; }
    public required bool FullAutoEnabled { get; init; }
    public BotDifficulty? BotDifficulty { get; init; }
    public BotPersonality? BotPersonality { get; init; }
    public BotPersonality? ActiveBotPersonality { get; init; }
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
    public bool IsMortgaged { get; init; }
    public int HouseCount { get; init; }
    public bool HasHotel { get; init; }
    public bool HasLandmark { get; init; }
    public int HouseCost { get; init; }
}

public sealed class MonopolyStateSnapshot
{
    public required MonopolyTurnPhase Phase { get; init; }
    public required string? ActivePlayerId { get; init; }
    public required string? PendingDecisionPlayerId { get; init; }
    public required int AvailableHouses { get; init; }
    public required int AvailableHotels { get; init; }
    public int? PendingPurchaseCellId { get; init; }
    public int PendingPurchasePrice { get; init; }
    public string? PendingPurchaseOwnerPlayerId { get; init; }
    public string? PendingDebtToPlayerId { get; init; }
    public int PendingDebtAmount { get; init; }
    public string? PendingDebtReason { get; init; }
    public int CurrentJailFine { get; init; }
    public int LastDiceOne { get; init; }
    public int LastDiceTwo { get; init; }
    public int CityPriceGrowthRounds { get; init; }
    public bool IsFinalDuel { get; init; }
    public int FinalDuelRound { get; init; }
    public int FinalDuelRoundsRemaining { get; init; }
    public int FinalDuelGoReward { get; init; }
    public int FinalDuelRentBonusPercent { get; init; }
    public bool IsFinalDuelVoteEligible { get; init; }
    public bool IsFinalDuelVotePendingStart { get; init; }
    public int FinalDuelVoteYesCount { get; init; }
    public int FinalDuelVoteRequired { get; init; }
    public IReadOnlyList<string> FinalDuelVotedPlayerIds { get; init; } = Array.Empty<string>();
    public bool UpgradeUsedThisTurn { get; init; }
    public IReadOnlyList<int> UpgradeEligibleCellIds { get; init; } = Array.Empty<int>();
    public MonopolyAuctionSnapshot? ActiveAuction { get; init; }
    public MonopolyTradeSnapshot? ActiveTradeOffer { get; init; }
    public IReadOnlyList<MonopolyPlayerEconomySnapshot> PlayerEconomy { get; init; } =
        Array.Empty<MonopolyPlayerEconomySnapshot>();
}

public sealed class MonopolyAuctionSnapshot
{
    public required int CellId { get; init; }
    public required string CellName { get; init; }
    public required int CurrentBidAmount { get; init; }
    public required string? CurrentBidderPlayerId { get; init; }
    public required IReadOnlyList<string> EligiblePlayerIds { get; init; }
    public required IReadOnlyList<string> PassedPlayerIds { get; init; }
}

public sealed class MonopolyTradeSnapshot
{
    public required string FromPlayerId { get; init; }
    public required string ToPlayerId { get; init; }
    public required int CashGive { get; init; }
    public required int CashReceive { get; init; }
    public required IReadOnlyList<int> GiveCells { get; init; }
    public required IReadOnlyList<int> ReceiveCells { get; init; }
}

public sealed class MonopolyPlayerEconomySnapshot
{
    public required string PlayerId { get; init; }
    public required int Cash { get; init; }
    public required int AssetValue { get; init; }
    public required int NetWorth { get; init; }
    public required int PropertyCount { get; init; }
    public required int MonopolySetCount { get; init; }
    public required int Houses { get; init; }
    public required int Hotels { get; init; }
    public required int Landmarks { get; init; }
    public required int Mortgaged { get; init; }
    public required bool InJail { get; init; }
    public required bool IsBankrupt { get; init; }
}

public sealed class GameResultSnapshot
{
    public required string? WinnerPlayerId { get; init; }
    public required string? FinishReason { get; init; }
    public required IReadOnlyList<GamePlacementSnapshot> Placements { get; init; }
}

public sealed class GamePlacementSnapshot
{
    public required string PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required int AvatarId { get; init; }
    public required int Rank { get; init; }
    public required int Cash { get; init; }
    public required int NetWorth { get; init; }
    public required bool IsBankrupt { get; init; }
    public required string OutcomeReason { get; init; }
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
