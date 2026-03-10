using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule : IGameRoomModule
{
    private static readonly Dictionary<string, int> ColorGroupSetSize =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Brown"] = 2,
            ["Light Blue"] = 3,
            ["Pink"] = 3,
            ["Orange"] = 3,
            ["Red"] = 3,
            ["Yellow"] = 3,
            ["Green"] = 3,
            ["Dark Blue"] = 2
        };

    public string GameKey => GameCatalog.Monopoly;
    public string DisplayName => "เกมเศรษฐี";
    public string Description => "Monopoly Classic Economy แบบแอคชั่นเทิร์น";
    public bool IsAvailable => true;

    public ServiceResult<BoardOptions> BuildBoardOptionsForCreate(CreateRoomRequest request)
    {
        var boardOptions = new BoardOptions
        {
            GameMode = GameMode.Classic,
            BoardSize = MonopolyDefinitions.DefaultBoardCellCount,
            DensityMode = DensityMode.Low,
            OverflowMode = OverflowMode.StayPut,
            RuleOptions = new RuleOptions
            {
                ItemsEnabled = false,
                CheckpointShieldEnabled = false,
                ComebackBoostEnabled = false,
                LuckyRerollEnabled = false,
                LuckyRerollPerPlayer = 0,
                ForkPathEnabled = false,
                SnakeFrenzyEnabled = false,
                MercyLadderEnabled = false,
                TurnTimerEnabled = true,
                TurnSeconds = 20,
                RoundLimitEnabled = true,
                MaxRounds = 36,
                MarathonSpeedupEnabled = false
            }
        };

        return ServiceResult<BoardOptions>.Ok(boardOptions);
    }

    public string? ValidateRoomBeforeStart(GameRoom room)
    {
        return room.Players.Count < 2
            ? "ต้องมีผู้เล่นอย่างน้อย 2 คน"
            : null;
    }

    public void StartGame(GameRoom room)
    {
        var random = room.BoardOptions.Seed.HasValue
            ? new Random(room.BoardOptions.Seed.Value)
            : Random.Shared;

        room.Board = new BoardState
        {
            Size = MonopolyDefinitions.DefaultBoardCellCount,
            Jumps = Array.Empty<Jump>(),
            ForkCells = Array.Empty<ForkCell>(),
            JumpsByFrom = new Dictionary<int, Jump>(),
            ForksByCell = new Dictionary<int, ForkCell>()
        };

        var monopoly = new MonopolyRoomState();
        monopoly.Cells.AddRange(MonopolyBoardTemplate.CreateDefaultCells());
        room.Monopoly = monopoly;

        foreach (var player in room.Players)
        {
            ResetPlayerForMonopoly(player, room.HostPlayerId, waitingMode: false);
            monopoly.ConsecutiveDoublesByPlayer[player.PlayerId] = 0;
            monopoly.JailAttemptByPlayer[player.PlayerId] = 0;
            monopoly.JailFineByPlayer[player.PlayerId] = MonopolyDefinitions.JailFine;
            monopoly.ExtraTurnByPlayer[player.PlayerId] = false;
        }

        room.Status = GameStatus.Started;
        room.BoardOptions.RuleOptions.MaxRounds =
            ResolveEffectiveConfiguredRoundLimit(
                room.BoardOptions.RuleOptions.MaxRounds,
                room.Players.Count);
        room.CurrentTurnIndex = random.Next(0, room.Players.Count);
        room.TurnCounter = 0;
        room.CompletedRounds = 0;
        monopoly.CityPriceGrowthRounds = 0;
        monopoly.StartedPlayerCount = room.Players.Count;
        monopoly.FinalDuelActive = false;
        monopoly.FinalDuelStartCompletedRounds = 0;
        monopoly.FinalDuelVotePendingStart = false;
        monopoly.LastBankruptcyCompletedRound = null;
        monopoly.FinalDuelVotePlayerIds.Clear();
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

        var active = room.CurrentTurnPlayer;
        monopoly.ActivePlayerId = active?.PlayerId;
        monopoly.PendingDecisionPlayerId = active?.PlayerId;
        monopoly.Phase = active is null
            ? MonopolyTurnPhase.Finished
            : active.JailTurnsRemaining > 0
                ? MonopolyTurnPhase.AwaitJailDecision
                : MonopolyTurnPhase.AwaitRoll;
    }

    public void ResetFinishedGame(GameRoom room)
    {
        foreach (var player in room.Players)
        {
            ResetPlayerForMonopoly(player, room.HostPlayerId, waitingMode: true);
        }

        room.Status = GameStatus.Waiting;
        room.Board = null;
        room.Monopoly = null;
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
        var actionResult = SubmitGameAction(
            room,
            player,
            new SubmitGameActionRequest
            {
                RoomCode = request.RoomCode,
                ActionType = GameActionType.RollDice,
                Monopoly = new MonopolyActionPayload()
            },
            isAutoRoll);

        if (!actionResult.Success || actionResult.Value is null)
        {
            throw new InvalidOperationException(actionResult.Error ?? "ดำเนินการ RollDice ไม่สำเร็จ");
        }

        return actionResult.Value;
    }

    public ServiceResult<TurnResult> SubmitGameAction(
        GameRoom room,
        PlayerState actor,
        SubmitGameActionRequest request,
        bool isAutoAction)
    {
        if (room.Board is null || room.Monopoly is null)
        {
            return ServiceResult<TurnResult>.Fail("ห้องเกมเศรษฐียังไม่ถูกเตรียมกระดาน");
        }

        if (room.Status != GameStatus.Started)
        {
            return ServiceResult<TurnResult>.Fail("เกมยังไม่อยู่ในสถานะกำลังเล่น");
        }

        var state = room.Monopoly;
        if (!CanActorSubmitAction(state, actor, request.ActionType))
        {
            return ServiceResult<TurnResult>.Fail("ยังไม่ถึงสิทธิ์ของคุณในการทำแอคชั่นนี้");
        }

        if (actor.IsBankrupt &&
            request.ActionType != GameActionType.EndTurn &&
            request.ActionType != GameActionType.DeclareBankruptcy)
        {
            return ServiceResult<TurnResult>.Fail("ผู้เล่นนี้ล้มละลายแล้ว");
        }

        var execution = new ActionExecution(actor.Position)
        {
            AutoReason = isAutoAction
                ? (actor.Connected ? "TimerExpired" : "Disconnected")
                : null
        };

        var pendingDebtReasonBeforeSettle = state.PendingDebtReason;

        var error = request.ActionType switch
        {
            GameActionType.RollDice => HandleRollDice(room, state, actor, execution),
            GameActionType.PayJailFine => HandlePayJailFine(room, state, actor, execution),
            GameActionType.TryJailRoll => HandleTryJailRoll(room, state, actor, execution),
            GameActionType.BuyProperty => HandleBuyProperty(room, state, actor, execution),
            GameActionType.DeclinePurchase => HandleDeclinePurchase(room, state, actor, execution),
            GameActionType.BidAuction => "ระบบประมูลถูกปิดใช้งานแล้ว",
            GameActionType.PassAuction => "ระบบประมูลถูกปิดใช้งานแล้ว",
            GameActionType.BuildHouse => HandleBuildHouse(state, actor, request.Monopoly, execution),
            GameActionType.SellHouse => HandleSellHouse(state, actor, request.Monopoly, execution),
            GameActionType.Mortgage => HandleMortgage(room, state, actor, request.Monopoly, execution),
            GameActionType.Unmortgage => HandleUnmortgage(room, state, actor, request.Monopoly, execution),
            GameActionType.OfferTrade => "ระบบเทรดถูกปิดใช้งานแล้ว",
            GameActionType.AcceptTrade => "ระบบเทรดถูกปิดใช้งานแล้ว",
            GameActionType.RejectTrade => "ระบบเทรดถูกปิดใช้งานแล้ว",
            GameActionType.DeclareBankruptcy => HandleDeclareBankruptcy(room, state, actor, execution),
            GameActionType.SellProperty => HandleSellProperty(room, state, actor, request.Monopoly, execution),
            GameActionType.EndTurn => HandleEndTurn(room, state, actor, execution),
            _ => "ยังไม่รองรับแอคชั่นนี้"
        };

        if (error is not null)
        {
            return ServiceResult<TurnResult>.Fail(error);
        }

        TryAutoSettleDebt(room, state, actor, execution.Logs);
        TryRestoreRollAfterJailFineDebt(state, actor, pendingDebtReasonBeforeSettle, execution.Logs);
        TryActivateFinalDuel(room, state, execution.Logs);

        if (TryResolveFinish(room, state, execution.Logs, out var winnerPlayerId, out var finishReason, out var roundLimitTriggered))
        {
            room.Status = GameStatus.Finished;
            room.WinnerPlayerId = winnerPlayerId;
            room.FinishReason = finishReason;
            state.Phase = MonopolyTurnPhase.Finished;
            state.ActivePlayerId = null;
            state.PendingDecisionPlayerId = null;
        }
        else
        {
            room.Status = GameStatus.Started;
            room.WinnerPlayerId = null;
            room.FinishReason = null;
            roundLimitTriggered = false;
            winnerPlayerId = null;
            finishReason = null;
        }

        var logs = execution.Logs.Count > 0
            ? execution.Logs
            : new List<string> { "ดำเนินการเสร็จสิ้น" };

        var diceTotal = execution.DiceOne + execution.DiceTwo;

        return ServiceResult<TurnResult>.Ok(new TurnResult
        {
            RoomCode = room.RoomCode,
            PlayerId = actor.PlayerId,
            ActionType = request.ActionType,
            StartPosition = execution.StartPosition,
            DiceValue = diceTotal,
            DiceOne = execution.DiceOne,
            DiceTwo = execution.DiceTwo,
            IsDouble = execution.DiceOne > 0 && execution.DiceOne == execution.DiceTwo,
            ExtraTurnGranted = execution.ExtraTurnGranted,
            BaseDiceValue = diceTotal,
            EndPosition = actor.Position,
            RoundLimitTriggered = roundLimitTriggered,
            IsGameFinished = room.Status == GameStatus.Finished,
            WinnerPlayerId = winnerPlayerId,
            FinishReason = finishReason,
            AutoRollReason = execution.AutoReason,
            ActionSummary = logs[0],
            ActionLogs = logs
        });
    }
}
