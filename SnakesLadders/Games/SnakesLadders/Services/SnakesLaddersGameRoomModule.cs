using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class SnakesLaddersGameRoomModule(
    IBoardGenerator boardGenerator,
    IGameEngine gameEngine)
    : IGameRoomModule
{
    public string GameKey => GameCatalog.SnakesLadders;
    public string DisplayName => "Snakes & Ladders";
    public string Description => "เกมบันไดงูแบบเรียลไทม์ ปรับกติกาได้";
    public bool IsAvailable => true;

    public ServiceResult<BoardOptions> BuildBoardOptionsForCreate(CreateRoomRequest request)
    {
        var boardOptions = request.BoardOptions ?? new BoardOptions();
        boardOptions.Normalize();
        var validation = boardOptions.Validate();
        if (validation is not null)
        {
            return ServiceResult<BoardOptions>.Fail(validation);
        }

        return ServiceResult<BoardOptions>.Ok(boardOptions);
    }

    public string? ValidateRoomBeforeStart(GameRoom room)
    {
        room.BoardOptions.Normalize();
        return room.BoardOptions.Validate();
    }

    public void StartGame(GameRoom room)
    {
        var random = room.BoardOptions.Seed.HasValue
            ? new Random(room.BoardOptions.Seed.Value)
            : Random.Shared;
        room.Board = boardGenerator.Generate(room.BoardOptions, random);

        foreach (var player in room.Players)
        {
            player.Position = 1;
            player.Shields = 0;
            player.ConsecutiveSnakeHits = 0;
            player.MercyLadderPending = false;
            player.SnakeRepellentCharges = 0;
            player.LadderHackPending = false;
            player.AnchorTurnsRemaining = 0;
            player.ItemDryTurnStreak = 0;
            player.NextCheckpoint = Math.Max(1, room.BoardOptions.RuleOptions.CheckpointInterval);
            player.LuckyRerollsLeft = room.BoardOptions.RuleOptions.LuckyRerollEnabled
                ? room.BoardOptions.RuleOptions.LuckyRerollPerPlayer
                : 0;
        }

        room.Status = GameStatus.Started;
        room.CurrentTurnIndex = random.Next(0, room.Players.Count);
        room.TurnCounter = 0;
        room.CompletedRounds = 0;
        room.WinnerPlayerId = null;
        room.FinishReason = null;
        room.ActiveFrenzySnake = null;
        room.ActiveFrenzySnakeTurnsLeft = 0;
        room.FrenzyNoSpawnStreak = 0;
        room.NextItemRefreshAtTurnCounter = 0;
        room.ActiveItems.Clear();
        room.TemporaryJumps.Clear();
        room.BananaTraps.Clear();
        room.TurnDeadlineUtc = null;
        gameEngine.SeedRoomState(room);
    }

    public void ResetFinishedGame(GameRoom room)
    {
        var checkpoint = Math.Max(1, room.BoardOptions.RuleOptions.CheckpointInterval);
        foreach (var player in room.Players)
        {
            player.Position = 1;
            player.Shields = 0;
            player.ConsecutiveSnakeHits = 0;
            player.MercyLadderPending = false;
            player.SnakeRepellentCharges = 0;
            player.LadderHackPending = false;
            player.AnchorTurnsRemaining = 0;
            player.ItemDryTurnStreak = 0;
            player.NextCheckpoint = checkpoint;
            player.LuckyRerollsLeft = room.BoardOptions.RuleOptions.LuckyRerollEnabled
                ? room.BoardOptions.RuleOptions.LuckyRerollPerPlayer
                : 0;
            player.IsReady = player.PlayerId == room.HostPlayerId;
        }

        room.Status = GameStatus.Waiting;
        room.Board = null;
        room.CurrentTurnIndex = 0;
        room.TurnCounter = 0;
        room.CompletedRounds = 0;
        room.WinnerPlayerId = null;
        room.FinishReason = null;
        room.ActiveFrenzySnake = null;
        room.ActiveFrenzySnakeTurnsLeft = 0;
        room.FrenzyNoSpawnStreak = 0;
        room.NextItemRefreshAtTurnCounter = 0;
        room.ActiveItems.Clear();
        room.TemporaryJumps.Clear();
        room.BananaTraps.Clear();
        room.TurnDeadlineUtc = null;
    }

    public TurnResult ResolveTurn(
        GameRoom room,
        PlayerState player,
        RollDiceRequest request,
        bool isAutoRoll)
    {
        return gameEngine.ResolveTurn(
            room,
            player,
            request.UseLuckyReroll,
            request.ForkChoice,
            isAutoRoll);
    }

    public ServiceResult<TurnResult> SubmitGameAction(
        GameRoom room,
        PlayerState actor,
        SubmitGameActionRequest request,
        bool isAutoAction)
    {
        if (request.ActionType != GameActionType.RollDice)
        {
            return ServiceResult<TurnResult>.Fail("เกม Snakes & Ladders รองรับเฉพาะแอคชั่น RollDice");
        }

        var turn = ResolveTurn(
            room,
            actor,
            new RollDiceRequest
            {
                RoomCode = request.RoomCode,
                UseLuckyReroll = request.UseLuckyReroll,
                ForkChoice = request.ForkChoice
            },
            isAutoAction);
        return ServiceResult<TurnResult>.Ok(turn);
    }
}
