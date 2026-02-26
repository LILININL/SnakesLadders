namespace SnakesLadders.Domain;

public sealed record Jump(int From, int To, JumpType Type, bool IsTemporary = false);

public sealed record ForkCell(int Cell, int SafeTo, int RiskyTo);

public sealed class BoardState
{
    public required int Size { get; init; }
    public required IReadOnlyList<Jump> Jumps { get; init; }
    public required IReadOnlyList<ForkCell> ForkCells { get; init; }
    public required IReadOnlyDictionary<int, Jump> JumpsByFrom { get; init; }
    public required IReadOnlyDictionary<int, ForkCell> ForksByCell { get; init; }
}

public sealed class PlayerState
{
    public required string PlayerId { get; init; }
    public required string SessionId { get; init; }
    public required string ConnectionId { get; set; }
    public required string DisplayName { get; set; }
    public int AvatarId { get; set; } = 1;
    public int Position { get; set; } = 1;
    public bool Connected { get; set; } = true;
    public bool IsReady { get; set; }

    public int Shields { get; set; }
    public int NextCheckpoint { get; set; } = 50;
    public int LuckyRerollsLeft { get; set; }

    public int ConsecutiveSnakeHits { get; set; }
    public bool MercyLadderPending { get; set; }
}

public sealed class GameRoom
{
    public required string RoomCode { get; init; }
    public required BoardOptions BoardOptions { get; set; }
    public required string HostPlayerId { get; set; }

    public GameStatus Status { get; set; } = GameStatus.Waiting;
    public BoardState? Board { get; set; }

    public List<PlayerState> Players { get; } = new();
    public int CurrentTurnIndex { get; set; }
    public int TurnCounter { get; set; }
    public int CompletedRounds { get; set; }

    public string? WinnerPlayerId { get; set; }
    public string? FinishReason { get; set; }

    public Jump? ActiveFrenzySnake { get; set; }
    public int ActiveFrenzySnakeTurnsLeft { get; set; }
    public int FrenzyNoSpawnStreak { get; set; }
    public DateTimeOffset? TurnDeadlineUtc { get; set; }

    public PlayerState? CurrentTurnPlayer =>
        Players.Count == 0 ? null : Players[CurrentTurnIndex % Players.Count];

    public PlayerState? FindPlayer(string playerId) =>
        Players.FirstOrDefault(x => x.PlayerId == playerId);

    public PlayerState? FindPlayerBySession(string sessionId) =>
        Players.FirstOrDefault(x => x.SessionId == sessionId);
}

public sealed class TurnResult
{
    public required string RoomCode { get; init; }
    public required string PlayerId { get; init; }
    public required int StartPosition { get; init; }
    public required int DiceValue { get; init; }
    public int BaseDiceValue { get; init; }
    public int ComebackBoostAmount { get; init; }
    public required int EndPosition { get; init; }

    public bool ComebackBoostApplied { get; init; }
    public bool UsedLuckyReroll { get; init; }
    public int OverflowAmount { get; init; }

    public ForkPathChoice? ForkChoice { get; init; }
    public ForkCell? ForkCell { get; init; }

    public Jump? TriggeredJump { get; init; }
    public Jump? FrenzySnake { get; init; }
    public bool FrenzySnakeTriggered { get; init; }
    public bool FrenzySnakeBlockedByShield { get; init; }
    public bool ShieldBlockedSnake { get; init; }
    public bool MercyLadderApplied { get; init; }

    public int ShieldsEarned { get; init; }

    public bool RoundLimitTriggered { get; init; }
    public bool IsGameFinished { get; init; }
    public string? WinnerPlayerId { get; init; }
    public string? FinishReason { get; init; }
    public string? AutoRollReason { get; init; }
}
