using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class MonopolyGameRoomModule : IGameRoomModule
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
                MaxRounds = 80,
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
            monopoly.ExtraTurnByPlayer[player.PlayerId] = false;
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

        var error = request.ActionType switch
        {
            GameActionType.RollDice => HandleRollDice(room, state, actor, execution),
            GameActionType.PayJailFine => HandlePayJailFine(state, actor, execution),
            GameActionType.TryJailRoll => HandleTryJailRoll(room, state, actor, execution),
            GameActionType.BuyProperty => HandleBuyProperty(state, actor, execution),
            GameActionType.DeclinePurchase => HandleDeclinePurchase(room, state, actor, execution),
            GameActionType.BidAuction => HandleBidAuction(room, state, actor, request.Monopoly, execution),
            GameActionType.PassAuction => HandlePassAuction(room, state, actor, execution),
            GameActionType.BuildHouse => HandleBuildHouse(state, actor, request.Monopoly, execution),
            GameActionType.SellHouse => HandleSellHouse(state, actor, request.Monopoly, execution),
            GameActionType.Mortgage => HandleMortgage(state, actor, request.Monopoly, execution),
            GameActionType.Unmortgage => HandleUnmortgage(state, actor, request.Monopoly, execution),
            GameActionType.OfferTrade => HandleOfferTrade(room, state, actor, request.Monopoly, execution),
            GameActionType.AcceptTrade => HandleAcceptTrade(room, state, actor, execution),
            GameActionType.RejectTrade => HandleRejectTrade(state, actor, execution),
            GameActionType.DeclareBankruptcy => HandleDeclareBankruptcy(room, state, actor, execution),
            GameActionType.EndTurn => HandleEndTurn(room, state, actor, execution),
            _ => "ยังไม่รองรับแอคชั่นนี้"
        };

        if (error is not null)
        {
            return ServiceResult<TurnResult>.Fail(error);
        }

        TryAutoSettleDebt(room, state, actor, execution.Logs);

        if (TryResolveFinish(room, state, out var winnerPlayerId, out var finishReason, out var roundLimitTriggered))
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

    private static bool CanActorSubmitAction(
        MonopolyRoomState state,
        PlayerState actor,
        GameActionType actionType)
    {
        if (actionType is GameActionType.AcceptTrade or GameActionType.RejectTrade)
        {
            return state.Phase == MonopolyTurnPhase.AwaitTradeResponse &&
                   string.Equals(state.PendingDecisionPlayerId, actor.PlayerId, StringComparison.Ordinal);
        }

        if (actionType is GameActionType.BidAuction or GameActionType.PassAuction)
        {
            return state.Phase == MonopolyTurnPhase.AuctionInProgress &&
                   string.Equals(state.PendingDecisionPlayerId, actor.PlayerId, StringComparison.Ordinal);
        }

        return string.Equals(state.ActivePlayerId, actor.PlayerId, StringComparison.Ordinal);
    }

    private static string? HandleRollDice(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitRoll)
        {
            return "ยังทอยเต๋าไม่ได้ในตอนนี้";
        }

        var d1 = Random.Shared.Next(1, 7);
        var d2 = Random.Shared.Next(1, 7);
        execution.DiceOne = d1;
        execution.DiceTwo = d2;
        state.LastDiceOne = d1;
        state.LastDiceTwo = d2;

        var isDouble = d1 == d2;
        var currentDoubleCount = isDouble
            ? GetAndIncrement(state.ConsecutiveDoublesByPlayer, actor.PlayerId)
            : ResetAndGet(state.ConsecutiveDoublesByPlayer, actor.PlayerId);

        execution.Logs.Add($"ทอยได้ {d1} + {d2} = {d1 + d2}");

        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.Resolving;

        if (isDouble && currentDoubleCount >= 3)
        {
            SendPlayerToJail(state, actor, execution.Logs, "ทอยดับเบิล 3 ครั้งติด ถูกส่งเข้าคุก");
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            return null;
        }

        MovePlayerBy(room, state, actor, d1 + d2, execution.Logs);

        if (state.Phase == MonopolyTurnPhase.Resolving)
        {
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = actor.PlayerId;
        }

        if (isDouble && state.Phase is MonopolyTurnPhase.AwaitManage or MonopolyTurnPhase.AwaitEndTurn)
        {
            state.ExtraTurnByPlayer[actor.PlayerId] = true;
            execution.ExtraTurnGranted = true;
            execution.Logs.Add("ได้สิทธิ์เล่นต่ออีกตา (ทอยดับเบิล)");
        }
        else if (!state.ExtraTurnByPlayer.ContainsKey(actor.PlayerId))
        {
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
        }

        return null;
    }

    private static string? HandlePayJailFine(
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitJailDecision)
        {
            return "ยังไม่ถึงช่วงตัดสินใจตอนติดคุก";
        }

        if (actor.JailTurnsRemaining <= 0)
        {
            return "ผู้เล่นไม่ได้อยู่ในคุก";
        }

        if (actor.Cash < MonopolyDefinitions.JailFine)
        {
            return "เงินไม่พอจ่ายค่าปรับออกจากคุก";
        }

        actor.Cash -= MonopolyDefinitions.JailFine;
        actor.JailTurnsRemaining = 0;
        state.JailAttemptByPlayer[actor.PlayerId] = 0;
        state.Phase = MonopolyTurnPhase.AwaitRoll;
        state.PendingDecisionPlayerId = actor.PlayerId;
        execution.Logs.Add($"จ่ายค่าปรับ ฿{MonopolyDefinitions.JailFine} และออกจากคุกแล้ว");
        return null;
    }

    private static string? HandleTryJailRoll(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitJailDecision)
        {
            return "ยังไม่ถึงช่วงตัดสินใจตอนติดคุก";
        }

        if (actor.JailTurnsRemaining <= 0)
        {
            return "ผู้เล่นไม่ได้อยู่ในคุก";
        }

        var d1 = Random.Shared.Next(1, 7);
        var d2 = Random.Shared.Next(1, 7);
        execution.DiceOne = d1;
        execution.DiceTwo = d2;
        state.LastDiceOne = d1;
        state.LastDiceTwo = d2;

        var attempts = GetAndIncrement(state.JailAttemptByPlayer, actor.PlayerId);
        var isDouble = d1 == d2;

        execution.Logs.Add($"ทอยแก้คุกได้ {d1} + {d2} = {d1 + d2}");

        ClearTransientActionState(state, keepDebt: true);

        if (isDouble)
        {
            actor.JailTurnsRemaining = 0;
            state.JailAttemptByPlayer[actor.PlayerId] = 0;
            state.Phase = MonopolyTurnPhase.Resolving;
            execution.Logs.Add("ทอยดับเบิลสำเร็จ ออกจากคุกและเดินต่อได้");
            MovePlayerBy(room, state, actor, d1 + d2, execution.Logs);
            if (state.Phase == MonopolyTurnPhase.Resolving)
            {
                state.Phase = MonopolyTurnPhase.AwaitManage;
            }

            state.PendingDecisionPlayerId = actor.PlayerId;
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
            return null;
        }

        if (attempts >= MonopolyDefinitions.MaxJailAttempts)
        {
            actor.JailTurnsRemaining = 0;
            state.JailAttemptByPlayer[actor.PlayerId] = 0;
            actor.Cash -= MonopolyDefinitions.JailFine;
            state.Phase = MonopolyTurnPhase.Resolving;
            execution.Logs.Add($"พยายามครบ {MonopolyDefinitions.MaxJailAttempts} ครั้ง จ่ายค่าปรับ ฿{MonopolyDefinitions.JailFine} และเดินต่อ");
            MovePlayerBy(room, state, actor, d1 + d2, execution.Logs);
            if (state.Phase == MonopolyTurnPhase.Resolving)
            {
                state.Phase = MonopolyTurnPhase.AwaitManage;
            }

            state.PendingDecisionPlayerId = actor.PlayerId;
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
            return null;
        }

        actor.JailTurnsRemaining = Math.Max(1, MonopolyDefinitions.MaxJailAttempts - attempts + 1);
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        state.PendingDecisionPlayerId = actor.PlayerId;
        state.ExtraTurnByPlayer[actor.PlayerId] = false;
        execution.Logs.Add($"ยังออกจากคุกไม่ได้ (พยายามแล้ว {attempts}/{MonopolyDefinitions.MaxJailAttempts})");
        return null;
    }

    private static string? HandleBuyProperty(
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitPurchaseDecision)
        {
            return "ตอนนี้ไม่มีทรัพย์สินให้ตัดสินใจซื้อ";
        }

        var cell = ResolvePendingPurchaseCell(state);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่รอการตัดสินใจ";
        }

        if (!string.IsNullOrWhiteSpace(cell.OwnerPlayerId))
        {
            return "ทรัพย์สินนี้ถูกซื้อไปแล้ว";
        }

        if (actor.Cash < cell.Price)
        {
            return "เงินไม่พอซื้อทรัพย์สินนี้";
        }

        actor.Cash -= cell.Price;
        cell.OwnerPlayerId = actor.PlayerId;
        cell.IsMortgaged = false;
        cell.HouseCount = 0;
        cell.HasHotel = false;

        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = actor.PlayerId;

        execution.Logs.Add($"ซื้อ {cell.Name} ในราคา ฿{cell.Price}");
        return null;
    }

    private static string? HandleDeclinePurchase(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitPurchaseDecision)
        {
            return "ตอนนี้ไม่มีทรัพย์สินให้ตัดสินใจ";
        }

        var cell = ResolvePendingPurchaseCell(state);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่รอการประมูล";
        }

        execution.Logs.Add($"ปฏิเสธการซื้อ {cell.Name}");

        var auction = new MonopolyAuctionState
        {
            CellId = cell.Cell,
            CurrentBidAmount = 0,
            CurrentBidderPlayerId = null,
            TurnIndex = 0
        };

        foreach (var player in ResolveAuctionTurnOrder(room, state.ActivePlayerId))
        {
            if (player.IsBankrupt || player.Cash <= 0)
            {
                continue;
            }

            auction.EligiblePlayerIds.Add(player.PlayerId);
            auction.TurnOrder.Add(player.PlayerId);
        }

        if (auction.TurnOrder.Count == 0)
        {
            ClearTransientActionState(state, keepDebt: true);
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = actor.PlayerId;
            execution.Logs.Add("ไม่มีผู้เล่นที่มีสิทธิ์ประมูล ทรัพย์สินยังเป็นของธนาคาร");
            return null;
        }

        state.ActiveAuction = auction;
        state.Phase = MonopolyTurnPhase.AuctionInProgress;
        state.PendingDecisionPlayerId = auction.TurnOrder[0];
        state.PendingPurchaseCellId = cell.Cell;
        execution.Logs.Add($"เริ่มประมูล {cell.Name}");
        return null;
    }

    private static string? HandleBidAuction(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AuctionInProgress || state.ActiveAuction is null)
        {
            return "ขณะนี้ไม่มีการประมูล";
        }

        var auction = state.ActiveAuction;
        if (!auction.EligiblePlayerIds.Contains(actor.PlayerId) ||
            auction.PassedPlayerIds.Contains(actor.PlayerId))
        {
            return "คุณไม่มีสิทธิ์ประมูลในรอบนี้";
        }

        var bidAmount = payload?.BidAmount ?? 0;
        if (bidAmount <= auction.CurrentBidAmount)
        {
            return "ราคาประมูลต้องสูงกว่าราคาปัจจุบัน";
        }

        if (bidAmount > actor.Cash)
        {
            return "เงินไม่พอสำหรับการประมูลราคานี้";
        }

        auction.CurrentBidAmount = bidAmount;
        auction.CurrentBidderPlayerId = actor.PlayerId;
        execution.Logs.Add($"{actor.DisplayName} บิด ฿{bidAmount}");

        AdvanceAuctionTurn(room, state, execution.Logs);
        return null;
    }

    private static string? HandlePassAuction(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AuctionInProgress || state.ActiveAuction is null)
        {
            return "ขณะนี้ไม่มีการประมูล";
        }

        var auction = state.ActiveAuction;
        if (!auction.EligiblePlayerIds.Contains(actor.PlayerId))
        {
            return "คุณไม่มีสิทธิ์ผ่านการประมูลนี้";
        }

        auction.PassedPlayerIds.Add(actor.PlayerId);
        execution.Logs.Add($"{actor.DisplayName} ผ่านการบิด");

        AdvanceAuctionTurn(room, state, execution.Logs);
        return null;
    }

    private static string? HandleBuildHouse(
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงจัดการทรัพย์สิน";
        }

        var cell = ResolveOwnedBuildCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่สามารถสร้างสิ่งปลูกสร้างได้";
        }

        if (!OwnsFullColorSet(state, actor.PlayerId, cell.ColorGroup) ||
            HasMortgagedInColorSet(state, actor.PlayerId, cell.ColorGroup))
        {
            return "ต้องถือชุดสีครบและห้ามมีแปลงใดในชุดสีที่ติดจำนอง";
        }

        if (cell.HasHotel)
        {
            return "แปลงนี้มีโรงแรมอยู่แล้ว";
        }

        if (cell.HouseCount < 4)
        {
            if (!CanBuildEvenly(state, actor.PlayerId, cell))
            {
                return "ต้องสร้างแบบสมดุลทุกแปลงในชุดสี (Even Build)";
            }

            if (state.AvailableHouses <= 0)
            {
                return "บ้านในธนาคารหมดแล้ว";
            }

            if (actor.Cash < cell.HouseCost)
            {
                return "เงินไม่พอสร้างบ้าน";
            }

            actor.Cash -= cell.HouseCost;
            cell.HouseCount++;
            state.AvailableHouses--;
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            execution.Logs.Add($"สร้างบ้านที่ {cell.Name} (+1 บ้าน)");
            return null;
        }

        if (!CanBuildEvenly(state, actor.PlayerId, cell))
        {
            return "ต้องอัปเกรดแบบสมดุลทุกแปลงในชุดสี";
        }

        if (state.AvailableHotels <= 0)
        {
            return "โรงแรมในธนาคารหมดแล้ว";
        }

        if (actor.Cash < cell.HouseCost)
        {
            return "เงินไม่พออัปเกรดเป็นโรงแรม";
        }

        actor.Cash -= cell.HouseCost;
        cell.HouseCount = 0;
        cell.HasHotel = true;
        state.AvailableHotels--;
        state.AvailableHouses += 4;
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        execution.Logs.Add($"อัปเกรด {cell.Name} เป็นโรงแรม");
        return null;
    }

    private static string? HandleSellHouse(
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงจัดการทรัพย์สิน";
        }

        var cell = ResolveOwnedBuildCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่สามารถขายสิ่งปลูกสร้างได้";
        }

        if (!CanSellEvenly(state, actor.PlayerId, cell))
        {
            return "ต้องขายแบบสมดุลทุกแปลงในชุดสี (Even Build)";
        }

        if (cell.HasHotel)
        {
            if (state.AvailableHouses < 4)
            {
                return "ไม่สามารถรื้อโรงแรมได้ เพราะบ้านในธนาคารไม่พอสำหรับแปลงระดับ 4";
            }

            cell.HasHotel = false;
            cell.HouseCount = 4;
            state.AvailableHotels++;
            state.AvailableHouses -= 4;
            actor.Cash += Math.Max(1, cell.HouseCost / 2);
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            execution.Logs.Add($"ขายโรงแรมที่ {cell.Name}");
            return null;
        }

        if (cell.HouseCount <= 0)
        {
            return "แปลงนี้ไม่มีบ้านให้ขาย";
        }

        cell.HouseCount--;
        state.AvailableHouses++;
        actor.Cash += Math.Max(1, cell.HouseCost / 2);
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        execution.Logs.Add($"ขายบ้านที่ {cell.Name} (-1 บ้าน)");
        return null;
    }

    private static string? HandleMortgage(
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงจัดการทรัพย์สิน";
        }

        var cell = ResolveOwnedAssetCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่สามารถจำนองได้";
        }

        if (cell.IsMortgaged)
        {
            return "ทรัพย์สินนี้ถูกจำนองอยู่แล้ว";
        }

        if (cell.HouseCount > 0 || cell.HasHotel)
        {
            return "ต้องขายสิ่งปลูกสร้างก่อนจำนอง";
        }

        var value = MortgageValue(cell);
        cell.IsMortgaged = true;
        actor.Cash += value;
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        execution.Logs.Add($"จำนอง {cell.Name} รับเงิน ฿{value}");
        return null;
    }

    private static string? HandleUnmortgage(
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงจัดการทรัพย์สิน";
        }

        var cell = ResolveOwnedAssetCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่ต้องการไถ่ถอน";
        }

        if (!cell.IsMortgaged)
        {
            return "ทรัพย์สินนี้ไม่ได้จำนองอยู่";
        }

        var cost = UnmortgageCost(cell);
        if (actor.Cash < cost)
        {
            return "เงินไม่พอสำหรับไถ่ถอน";
        }

        actor.Cash -= cost;
        cell.IsMortgaged = false;
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        execution.Logs.Add($"ไถ่ถอน {cell.Name} จ่าย ฿{cost}");
        return null;
    }

    private static string? HandleOfferTrade(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงเสนอเทรด";
        }

        if (state.ActiveTradeOffer is not null)
        {
            return "มีข้อเสนอเทรดค้างอยู่แล้ว";
        }

        var targetPlayerId = payload?.TargetPlayerId?.Trim();
        if (string.IsNullOrWhiteSpace(targetPlayerId) ||
            string.Equals(targetPlayerId, actor.PlayerId, StringComparison.Ordinal))
        {
            return "ต้องระบุผู้เล่นเป้าหมายที่ถูกต้อง";
        }

        var target = room.FindPlayer(targetPlayerId);
        if (target is null || target.IsBankrupt)
        {
            return "ไม่พบผู้เล่นเป้าหมายที่พร้อมเทรด";
        }

        var trade = payload?.TradeOffer;
        if (trade is null)
        {
            return "ข้อมูลข้อเสนอเทรดไม่ครบ";
        }

        var cashGive = Math.Max(0, trade.CashGive);
        var cashReceive = Math.Max(0, trade.CashReceive);
        var giveCells = DistinctCells(trade.GiveCells);
        var receiveCells = DistinctCells(trade.ReceiveCells);

        if (cashGive > actor.Cash)
        {
            return "เงินที่เสนอให้มากกว่ายอดเงินปัจจุบัน";
        }

        if (giveCells.Count == 0 && receiveCells.Count == 0 && cashGive == 0 && cashReceive == 0)
        {
            return "ข้อเสนอเทรดว่างเปล่า";
        }

        var giveValid = ValidateTradeCells(state, actor.PlayerId, giveCells, out var giveError);
        var receiveValid = ValidateTradeCells(state, target.PlayerId, receiveCells, out var receiveError);
        if (!giveValid || !receiveValid)
        {
            return giveError ?? receiveError ?? "ข้อมูลเทรดไม่ถูกต้อง";
        }

        state.ActiveTradeOffer = new MonopolyTradeOfferState
        {
            FromPlayerId = actor.PlayerId,
            ToPlayerId = target.PlayerId,
            CashGive = cashGive,
            CashReceive = cashReceive,
            GiveCells = giveCells,
            ReceiveCells = receiveCells,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        state.Phase = MonopolyTurnPhase.AwaitTradeResponse;
        state.PendingDecisionPlayerId = target.PlayerId;
        execution.Logs.Add($"ส่งข้อเสนอเทรดถึง {target.DisplayName}");
        return null;
    }

    private static string? HandleAcceptTrade(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitTradeResponse || state.ActiveTradeOffer is null)
        {
            return "ไม่มีข้อเสนอเทรดให้ตอบรับ";
        }

        var offer = state.ActiveTradeOffer;
        if (!string.Equals(actor.PlayerId, offer.ToPlayerId, StringComparison.Ordinal))
        {
            return "มีเพียงผู้เล่นเป้าหมายเท่านั้นที่ตอบรับเทรดได้";
        }

        var fromPlayer = room.FindPlayer(offer.FromPlayerId);
        var toPlayer = room.FindPlayer(offer.ToPlayerId);
        if (fromPlayer is null || toPlayer is null || fromPlayer.IsBankrupt || toPlayer.IsBankrupt)
        {
            return "ผู้เล่นที่เกี่ยวข้องกับเทรดไม่พร้อมใช้งาน";
        }

        if (fromPlayer.Cash < offer.CashGive)
        {
            return "ผู้เสนอเทรดมีเงินไม่พอแล้ว";
        }

        if (toPlayer.Cash < offer.CashReceive)
        {
            return "คุณมีเงินไม่พอสำหรับตอบรับข้อเสนอนี้";
        }

        var giveValid = ValidateTradeCells(state, fromPlayer.PlayerId, offer.GiveCells, out var giveError);
        var receiveValid = ValidateTradeCells(state, toPlayer.PlayerId, offer.ReceiveCells, out var receiveError);
        if (!giveValid || !receiveValid)
        {
            return giveError ?? receiveError ?? "ทรัพย์สินสำหรับเทรดไม่ถูกต้องแล้ว";
        }

        fromPlayer.Cash -= offer.CashGive;
        toPlayer.Cash += offer.CashGive;

        toPlayer.Cash -= offer.CashReceive;
        fromPlayer.Cash += offer.CashReceive;

        foreach (var cellId in offer.GiveCells)
        {
            var cell = state.FindCell(cellId);
            if (cell is not null)
            {
                cell.OwnerPlayerId = toPlayer.PlayerId;
            }
        }

        foreach (var cellId in offer.ReceiveCells)
        {
            var cell = state.FindCell(cellId);
            if (cell is not null)
            {
                cell.OwnerPlayerId = fromPlayer.PlayerId;
            }
        }

        state.ActiveTradeOffer = null;
        state.PendingDecisionPlayerId = state.ActivePlayerId;
        state.Phase = MonopolyTurnPhase.AwaitManage;
        execution.Logs.Add($"{actor.DisplayName} ตอบรับเทรดสำเร็จ");
        return null;
    }

    private static string? HandleRejectTrade(
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase != MonopolyTurnPhase.AwaitTradeResponse || state.ActiveTradeOffer is null)
        {
            return "ไม่มีข้อเสนอเทรดให้ปฏิเสธ";
        }

        if (!string.Equals(actor.PlayerId, state.ActiveTradeOffer.ToPlayerId, StringComparison.Ordinal))
        {
            return "มีเพียงผู้เล่นเป้าหมายเท่านั้นที่ปฏิเสธเทรดได้";
        }

        state.ActiveTradeOffer = null;
        state.PendingDecisionPlayerId = state.ActivePlayerId;
        state.Phase = MonopolyTurnPhase.AwaitManage;
        execution.Logs.Add($"{actor.DisplayName} ปฏิเสธข้อเสนอเทรด");
        return null;
    }

    private static string? HandleDeclareBankruptcy(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase == MonopolyTurnPhase.Finished)
        {
            return "เกมจบแล้ว";
        }

        ApplyBankruptcy(room, state, actor, execution.Logs, "ประกาศล้มละลาย");
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        state.PendingDecisionPlayerId = actor.PlayerId;
        return null;
    }

    private static string? HandleEndTurn(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังจบเทิร์นไม่ได้ในตอนนี้";
        }

        if (actor.Cash < 0 || (state.PendingDebtAmount > 0 && state.PendingDebtToPlayerId is not null))
        {
            ApplyBankruptcy(room, state, actor, execution.Logs, "ไม่สามารถชำระหนี้ได้");
        }

        room.TurnCounter++;

        if (!actor.IsBankrupt &&
            state.ExtraTurnByPlayer.TryGetValue(actor.PlayerId, out var extraTurn) &&
            extraTurn)
        {
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
            ClearTransientActionState(state, keepDebt: false);
            state.ActivePlayerId = actor.PlayerId;
            state.PendingDecisionPlayerId = actor.PlayerId;
            state.Phase = actor.JailTurnsRemaining > 0
                ? MonopolyTurnPhase.AwaitJailDecision
                : MonopolyTurnPhase.AwaitRoll;
            execution.ExtraTurnGranted = true;
            execution.Logs.Add("จบเทิร์นและได้เล่นต่ออีกตา");
            return null;
        }

        AdvanceTurn(room, state);
        execution.Logs.Add("จบเทิร์น");
        return null;
    }

    private static void MovePlayerBy(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        int steps,
        List<string> logs)
    {
        var boardSize = MonopolyDefinitions.DefaultBoardCellCount;
        var start = Math.Clamp(player.Position, 1, boardSize);
        var rawTarget = start + steps;
        var end = ((rawTarget - 1) % boardSize) + 1;

        if (rawTarget > boardSize)
        {
            player.Cash += MonopolyDefinitions.PassGoCash;
            logs.Add($"ผ่าน GO รับเงิน ฿{MonopolyDefinitions.PassGoCash}");
        }

        player.Position = end;
        ResolveLanding(room, state, player, logs, depth: 0);
    }

    private static void ResolveLanding(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        int depth)
    {
        if (depth > 6)
        {
            logs.Add("ระบบตัดการ resolve เพิ่มเติมเพื่อป้องกันลูปเหตุการณ์");
            return;
        }

        var landing = state.FindCell(player.Position);
        if (landing is null)
        {
            logs.Add($"เดินถึงช่อง {player.Position}");
            return;
        }

        switch (landing.Type)
        {
            case MonopolyCellType.Go:
                logs.Add("ถึง GO");
                return;
            case MonopolyCellType.Property:
            case MonopolyCellType.Railroad:
            case MonopolyCellType.Utility:
                ResolveAssetLanding(room, state, player, landing, logs);
                return;
            case MonopolyCellType.Tax:
                ResolveTaxLanding(state, player, landing, logs);
                return;
            case MonopolyCellType.Chance:
                ResolveChanceEvent(room, state, player, logs, depth);
                return;
            case MonopolyCellType.CommunityChest:
                ResolveCommunityEvent(room, state, player, logs, depth);
                return;
            case MonopolyCellType.Jail:
                logs.Add("แวะเยี่ยมคุก");
                return;
            case MonopolyCellType.FreeParking:
                logs.Add("Free Parking (กติกามาตรฐาน: ไม่มีโบนัส)");
                return;
            case MonopolyCellType.GoToJail:
                SendPlayerToJail(state, player, logs, "ตกช่อง Go To Jail");
                return;
            default:
                logs.Add($"เดินถึง {landing.Name}");
                return;
        }
    }

    private static void ResolveAssetLanding(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState landing,
        List<string> logs)
    {
        if (string.IsNullOrWhiteSpace(landing.OwnerPlayerId))
        {
            if (landing.Price <= 0)
            {
                logs.Add($"เดินถึง {landing.Name}");
                return;
            }

            state.PendingPurchaseCellId = landing.Cell;
            state.PendingDecisionPlayerId = player.PlayerId;
            state.Phase = MonopolyTurnPhase.AwaitPurchaseDecision;
            if (player.Cash < landing.Price)
            {
                var shortfall = landing.Price - player.Cash;
                logs.Add(
                    $"{landing.Name} ยังไม่มีเจ้าของ: เงินคุณไม่พอซื้อ (ขาด ฿{shortfall}) แนะนำให้ปฏิเสธเพื่อเริ่มประมูล");
            }
            else
            {
                logs.Add($"{landing.Name} ยังไม่มีเจ้าของ: ซื้อ (฿{landing.Price}) หรือปฏิเสธเพื่อเริ่มประมูล");
            }
            return;
        }

        if (string.Equals(landing.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal))
        {
            logs.Add($"ถึง {landing.Name} (ทรัพย์สินของคุณ)");
            return;
        }

        if (landing.IsMortgaged)
        {
            logs.Add($"ถึง {landing.Name} แต่เจ้าของจำนองอยู่ จึงไม่คิดค่าเช่า");
            return;
        }

        var owner = room.FindPlayer(landing.OwnerPlayerId);
        if (owner is null || owner.IsBankrupt)
        {
            logs.Add($"ถึง {landing.Name} แต่ไม่พบเจ้าของที่พร้อมรับค่าเช่า");
            return;
        }

        var rent = CalculateRent(room, state, landing, owner.PlayerId);
        if (rent <= 0)
        {
            logs.Add($"ถึง {landing.Name}");
            return;
        }

        ChargePlayer(room, state, player, owner, rent, logs, $"ค่าเช่า {landing.Name}");
    }

    private static void ResolveTaxLanding(
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState landing,
        List<string> logs)
    {
        var fee = Math.Max(0, landing.Fee);
        if (fee <= 0)
        {
            logs.Add($"ถึง {landing.Name}");
            return;
        }

        player.Cash -= fee;
        state.PendingDebtToPlayerId = null;
        state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
        logs.Add($"จ่ายภาษี {landing.Name} จำนวน ฿{fee}");
    }

    private static void ResolveChanceEvent(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        int depth)
    {
        var idx = Math.Abs(state.ChanceCursor++) % 8;

        switch (idx)
        {
            case 0:
                MovePlayerToCell(room, state, player, 1, logs, depth, "โอกาส: ไปช่องเริ่มต้น (GO)");
                return;
            case 1:
                player.Cash += 50;
                logs.Add("โอกาส: รับเงินปันผลจากธนาคาร ฿50");
                return;
            case 2:
                player.Cash -= 15;
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
                logs.Add("โอกาส: โดนค่าปรับความเร็ว -฿15");
                return;
            case 3:
                MovePlayerBack(room, state, player, 3, logs, depth, "โอกาส: ถอยหลัง 3 ช่อง");
                return;
            case 4:
                SendPlayerToJail(state, player, logs, "โอกาส: เข้าคุกทันที");
                return;
            case 5:
                player.Cash += 150;
                logs.Add("โอกาส: เงินกู้สิ่งปลูกสร้างครบกำหนด +฿150");
                return;
            case 6:
                MovePlayerToCell(room, state, player, 25, logs, depth, "โอกาส: ไปที่ สุราษฎร์ธานี");
                return;
            default:
                player.Cash -= 75;
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
                logs.Add("โอกาส: จ่ายภาษีคนจน -฿75");
                return;
        }
    }

    private static void ResolveCommunityEvent(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        int depth)
    {
        var idx = Math.Abs(state.CommunityCursor++) % 8;

        switch (idx)
        {
            case 0:
                player.Cash += 200;
                logs.Add("การ์ดชุมชน: ธนาคารจ่ายคืนให้คุณ +฿200");
                return;
            case 1:
                player.Cash -= 50;
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
                logs.Add("การ์ดชุมชน: จ่ายค่าหมอ -฿50");
                return;
            case 2:
                player.Cash += 50;
                logs.Add("การ์ดชุมชน: ขายหุ้นได้กำไร +฿50");
                return;
            case 3:
                SendPlayerToJail(state, player, logs, "การ์ดชุมชน: ไปเรือนจำ");
                return;
            case 4:
                player.Cash += 20;
                logs.Add("การ์ดชุมชน: คืนภาษี +฿20");
                return;
            case 5:
                player.Cash -= 150;
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
                logs.Add("การ์ดชุมชน: จ่ายค่าเล่าเรียน -฿150");
                return;
            case 6:
                player.Cash += 25;
                logs.Add("การ์ดชุมชน: รับค่าที่ปรึกษา +฿25");
                return;
            default:
                player.Cash += 100;
                logs.Add("การ์ดชุมชน: เงินกองทุนท่องเที่ยวครบกำหนด +฿100");
                return;
        }
    }

    private static void MovePlayerToCell(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        int targetCell,
        List<string> logs,
        int depth,
        string reason)
    {
        var boardSize = MonopolyDefinitions.DefaultBoardCellCount;
        var current = Math.Clamp(player.Position, 1, boardSize);
        var target = Math.Clamp(targetCell, 1, boardSize);

        if (target < current)
        {
            player.Cash += MonopolyDefinitions.PassGoCash;
            logs.Add($"{reason} และผ่าน GO รับ ฿{MonopolyDefinitions.PassGoCash}");
        }
        else
        {
            logs.Add(reason);
        }

        player.Position = target;
        ResolveLanding(room, state, player, logs, depth + 1);
    }

    private static void MovePlayerBack(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        int steps,
        List<string> logs,
        int depth,
        string reason)
    {
        var boardSize = MonopolyDefinitions.DefaultBoardCellCount;
        var target = player.Position - Math.Max(1, steps);
        while (target <= 0)
        {
            target += boardSize;
        }

        player.Position = target;
        logs.Add(reason);
        ResolveLanding(room, state, player, logs, depth + 1);
    }

    private static void AdvanceAuctionTurn(
        GameRoom room,
        MonopolyRoomState state,
        List<string> logs)
    {
        var auction = state.ActiveAuction;
        if (auction is null)
        {
            return;
        }

        if (TryFinalizeAuction(room, state, logs))
        {
            return;
        }

        if (auction.TurnOrder.Count == 0)
        {
            FinalizeAuction(room, state, null, logs);
            return;
        }

        for (var offset = 1; offset <= auction.TurnOrder.Count; offset++)
        {
            var idx = (auction.TurnIndex + offset) % auction.TurnOrder.Count;
            var candidateId = auction.TurnOrder[idx];
            var candidate = room.FindPlayer(candidateId);
            if (candidate is null || candidate.IsBankrupt)
            {
                continue;
            }

            if (!auction.EligiblePlayerIds.Contains(candidateId) ||
                auction.PassedPlayerIds.Contains(candidateId))
            {
                continue;
            }

            auction.TurnIndex = idx;
            state.PendingDecisionPlayerId = candidateId;
            return;
        }

        FinalizeAuction(room, state, null, logs);
    }

    private static bool TryFinalizeAuction(
        GameRoom room,
        MonopolyRoomState state,
        List<string> logs)
    {
        var auction = state.ActiveAuction;
        if (auction is null)
        {
            return true;
        }

        var activePlayers = auction.TurnOrder
            .Where(playerId =>
            {
                if (auction.PassedPlayerIds.Contains(playerId))
                {
                    return false;
                }

                var player = room.FindPlayer(playerId);
                return player is not null && !player.IsBankrupt;
            })
            .ToArray();

        if (auction.CurrentBidderPlayerId is not null)
        {
            if (activePlayers.Length == 0 ||
                (activePlayers.Length == 1 &&
                 string.Equals(activePlayers[0], auction.CurrentBidderPlayerId, StringComparison.Ordinal)))
            {
                FinalizeAuction(room, state, auction.CurrentBidderPlayerId, logs);
                return true;
            }

            return false;
        }

        if (activePlayers.Length == 0)
        {
            FinalizeAuction(room, state, null, logs);
            return true;
        }

        return false;
    }

    private static void FinalizeAuction(
        GameRoom room,
        MonopolyRoomState state,
        string? winnerPlayerId,
        List<string> logs)
    {
        var auction = state.ActiveAuction;
        var cell = auction is null ? null : state.FindCell(auction.CellId);
        if (auction is null || cell is null)
        {
            ClearTransientActionState(state, keepDebt: true);
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = state.ActivePlayerId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(winnerPlayerId))
        {
            var winner = room.FindPlayer(winnerPlayerId);
            var price = Math.Max(1, auction.CurrentBidAmount);
            if (winner is not null && !winner.IsBankrupt && winner.Cash >= price)
            {
                winner.Cash -= price;
                cell.OwnerPlayerId = winner.PlayerId;
                cell.IsMortgaged = false;
                cell.HouseCount = 0;
                cell.HasHotel = false;
                logs.Add($"{winner.DisplayName} ชนะประมูล {cell.Name} ที่ราคา ฿{price}");
            }
            else
            {
                logs.Add($"ยกเลิกผลประมูล {cell.Name} เพราะผู้ชนะไม่มีเงินพอ");
            }
        }
        else
        {
            logs.Add($"ไม่มีผู้ชนะประมูล {cell.Name}");
        }

        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = state.ActivePlayerId;
    }

    private static int CalculateRent(
        GameRoom room,
        MonopolyRoomState state,
        MonopolyCellState landing,
        string ownerPlayerId)
    {
        if (landing.IsMortgaged)
        {
            return 0;
        }

        return landing.Type switch
        {
            MonopolyCellType.Property => CalculatePropertyRent(state, landing, ownerPlayerId),
            MonopolyCellType.Railroad => CalculateRailroadRent(state, ownerPlayerId),
            MonopolyCellType.Utility => CalculateUtilityRent(state, ownerPlayerId),
            _ => Math.Max(0, landing.Rent)
        };
    }

    private static int CalculatePropertyRent(
        MonopolyRoomState state,
        MonopolyCellState landing,
        string ownerPlayerId)
    {
        var baseRent = Math.Max(0, landing.Rent);
        if (baseRent == 0)
        {
            return 0;
        }

        if (landing.HasHotel)
        {
            return baseRent * 125;
        }

        if (landing.HouseCount > 0)
        {
            return landing.HouseCount switch
            {
                1 => baseRent * 5,
                2 => baseRent * 15,
                3 => baseRent * 45,
                _ => baseRent * 80
            };
        }

        return OwnsFullColorSet(state, ownerPlayerId, landing.ColorGroup)
            ? baseRent * 2
            : baseRent;
    }

    private static int CalculateRailroadRent(MonopolyRoomState state, string ownerPlayerId)
    {
        var count = state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Railroad &&
            !cell.IsMortgaged &&
            string.Equals(cell.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal));

        if (count <= 0)
        {
            return 0;
        }

        return 25 * (int)Math.Pow(2, count - 1);
    }

    private static int CalculateUtilityRent(MonopolyRoomState state, string ownerPlayerId)
    {
        var count = state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Utility &&
            !cell.IsMortgaged &&
            string.Equals(cell.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal));

        if (count <= 0)
        {
            return 0;
        }

        var diceTotal = Math.Max(2, state.LastDiceOne + state.LastDiceTwo);
        return (count >= 2 ? 10 : 4) * diceTotal;
    }

    private static void ChargePlayer(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState debtor,
        PlayerState? creditor,
        int amount,
        List<string> logs,
        string reason)
    {
        var charge = Math.Max(0, amount);
        if (charge == 0)
        {
            return;
        }

        var beforeCash = Math.Max(0, debtor.Cash);
        var paidNow = Math.Min(beforeCash, charge);
        var remainingDebt = charge - paidNow;

        debtor.Cash -= charge;

        if (creditor is not null)
        {
            creditor.Cash += paidNow;
            if (remainingDebt > 0)
            {
                state.PendingDebtToPlayerId = creditor.PlayerId;
                state.PendingDebtAmount = remainingDebt;
                logs.Add($"{debtor.DisplayName} จ่าย {reason} ให้ {creditor.DisplayName} ได้บางส่วน ฿{paidNow} (ค้าง ฿{remainingDebt})");
            }
            else
            {
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = 0;
                logs.Add($"{debtor.DisplayName} จ่าย {reason} ให้ {creditor.DisplayName} จำนวน ฿{charge}");
            }

            return;
        }

        if (debtor.Cash < 0)
        {
            state.PendingDebtToPlayerId = null;
            state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Abs(debtor.Cash));
        }

        logs.Add($"{debtor.DisplayName} จ่าย {reason} จำนวน ฿{charge}");
    }

    private static void TryAutoSettleDebt(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        List<string> logs)
    {
        if (state.PendingDebtAmount <= 0 || string.IsNullOrWhiteSpace(state.PendingDebtToPlayerId))
        {
            if (actor.Cash >= 0 && state.PendingDebtToPlayerId is null)
            {
                state.PendingDebtAmount = 0;
            }
            return;
        }

        var creditor = room.FindPlayer(state.PendingDebtToPlayerId);
        if (creditor is null || creditor.IsBankrupt)
        {
            return;
        }

        var payment = Math.Min(Math.Max(0, actor.Cash), state.PendingDebtAmount);
        if (payment <= 0)
        {
            return;
        }

        actor.Cash -= payment;
        creditor.Cash += payment;
        state.PendingDebtAmount -= payment;

        logs.Add($"ชำระหนี้คงค้างให้ {creditor.DisplayName} เพิ่ม ฿{payment}");

        if (state.PendingDebtAmount <= 0)
        {
            state.PendingDebtAmount = 0;
            state.PendingDebtToPlayerId = null;
        }
    }

    private static void ApplyBankruptcy(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        string reason)
    {
        if (player.IsBankrupt)
        {
            return;
        }

        var creditor = !string.IsNullOrWhiteSpace(state.PendingDebtToPlayerId)
            ? room.FindPlayer(state.PendingDebtToPlayerId)
            : null;

        if (creditor is not null && !creditor.IsBankrupt)
        {
            creditor.Cash += Math.Max(0, player.Cash);
        }

        player.Cash = 0;
        player.IsBankrupt = true;
        player.JailTurnsRemaining = 0;

        foreach (var cell in state.Cells.Where(x => string.Equals(x.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal)))
        {
            if (creditor is not null && !creditor.IsBankrupt)
            {
                cell.OwnerPlayerId = creditor.PlayerId;
            }
            else
            {
                cell.OwnerPlayerId = null;
                cell.IsMortgaged = false;
                cell.HouseCount = 0;
                cell.HasHotel = false;
            }
        }

        state.ConsecutiveDoublesByPlayer[player.PlayerId] = 0;
        state.JailAttemptByPlayer[player.PlayerId] = 0;
        state.ExtraTurnByPlayer[player.PlayerId] = false;

        state.PendingDebtToPlayerId = null;
        state.PendingDebtAmount = 0;

        logs.Add($"{player.DisplayName} ล้มละลาย ({reason})");
    }

    private static void SendPlayerToJail(
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        string reason)
    {
        player.Position = MonopolyDefinitions.JailCell;
        player.JailTurnsRemaining = MonopolyDefinitions.MaxJailAttempts;
        state.JailAttemptByPlayer[player.PlayerId] = 0;
        state.ConsecutiveDoublesByPlayer[player.PlayerId] = 0;
        state.ExtraTurnByPlayer[player.PlayerId] = false;
        state.PendingPurchaseCellId = null;
        state.ActiveAuction = null;
        state.ActiveTradeOffer = null;
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        state.PendingDecisionPlayerId = player.PlayerId;
        logs.Add(reason);
    }

    private static void AdvanceTurn(GameRoom room, MonopolyRoomState state)
    {
        ClearTransientActionState(state, keepDebt: false);

        if (room.Players.Count == 0)
        {
            state.ActivePlayerId = null;
            state.PendingDecisionPlayerId = null;
            state.Phase = MonopolyTurnPhase.Finished;
            return;
        }

        var guard = 0;
        do
        {
            room.CurrentTurnIndex = (room.CurrentTurnIndex + 1) % room.Players.Count;
            if (room.CurrentTurnIndex == 0)
            {
                room.CompletedRounds++;
            }

            guard++;
        } while (guard <= room.Players.Count && room.Players[room.CurrentTurnIndex].IsBankrupt);

        var active = room.CurrentTurnPlayer;
        state.ActivePlayerId = active?.PlayerId;
        state.PendingDecisionPlayerId = active?.PlayerId;
        state.Phase = active is null
            ? MonopolyTurnPhase.Finished
            : active.JailTurnsRemaining > 0
                ? MonopolyTurnPhase.AwaitJailDecision
                : MonopolyTurnPhase.AwaitRoll;
    }

    private static void ClearTransientActionState(MonopolyRoomState state, bool keepDebt)
    {
        state.PendingPurchaseCellId = null;
        state.ActiveAuction = null;
        state.ActiveTradeOffer = null;

        if (!keepDebt)
        {
            state.PendingDebtAmount = 0;
            state.PendingDebtToPlayerId = null;
        }
    }

    private static bool TryResolveFinish(
        GameRoom room,
        MonopolyRoomState state,
        out string? winnerPlayerId,
        out string? finishReason,
        out bool roundLimitTriggered)
    {
        roundLimitTriggered = false;

        var alive = room.Players.Where(player => !player.IsBankrupt).ToArray();
        if (alive.Length <= 1)
        {
            winnerPlayerId = alive.FirstOrDefault()?.PlayerId;
            finishReason = "MonopolyLastStanding";
            return true;
        }

        var rules = room.BoardOptions.RuleOptions;
        var maxRounds = Math.Max(1, rules.MaxRounds);
        if (rules.RoundLimitEnabled && room.CompletedRounds >= maxRounds)
        {
            var leader = alive
                .OrderByDescending(player => CalculateNetWorth(state, player.PlayerId, player.Cash))
                .ThenByDescending(player => player.Cash)
                .ThenBy(player => room.Players.IndexOf(player))
                .First();

            winnerPlayerId = leader.PlayerId;
            finishReason = "RoundLimitNetWorth";
            roundLimitTriggered = true;
            return true;
        }

        winnerPlayerId = null;
        finishReason = null;
        return false;
    }

    private static int CalculateNetWorth(MonopolyRoomState state, string playerId, int cash)
    {
        var assetValue = 0;
        foreach (var cell in state.Cells.Where(cell =>
                     string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal)))
        {
            var baseValue = cell.IsMortgaged
                ? MortgageValue(cell)
                : Math.Max(0, cell.Price);
            var buildingValue = cell.HasHotel
                ? cell.HouseCost * 5
                : cell.HouseCost * Math.Max(0, cell.HouseCount);

            assetValue += baseValue + buildingValue;
        }

        return cash + assetValue;
    }

    private static MonopolyCellState? ResolvePendingPurchaseCell(MonopolyRoomState state)
    {
        if (!state.PendingPurchaseCellId.HasValue)
        {
            return null;
        }

        return state.FindCell(state.PendingPurchaseCellId.Value);
    }

    private static MonopolyCellState? ResolveOwnedAssetCell(
        MonopolyRoomState state,
        string playerId,
        int? cellId)
    {
        if (!cellId.HasValue)
        {
            return null;
        }

        var cell = state.FindCell(cellId.Value);
        if (cell is null)
        {
            return null;
        }

        if (cell.Type is not MonopolyCellType.Property and not MonopolyCellType.Railroad and not MonopolyCellType.Utility)
        {
            return null;
        }

        return string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal)
            ? cell
            : null;
    }

    private static MonopolyCellState? ResolveOwnedBuildCell(
        MonopolyRoomState state,
        string playerId,
        int? cellId)
    {
        var cell = ResolveOwnedAssetCell(state, playerId, cellId);
        if (cell is null)
        {
            return null;
        }

        if (cell.Type != MonopolyCellType.Property || string.IsNullOrWhiteSpace(cell.ColorGroup))
        {
            return null;
        }

        if (!ColorGroupSetSize.ContainsKey(cell.ColorGroup))
        {
            return null;
        }

        return cell;
    }

    private static bool OwnsFullColorSet(MonopolyRoomState state, string playerId, string? colorGroup)
    {
        if (string.IsNullOrWhiteSpace(colorGroup) ||
            !ColorGroupSetSize.TryGetValue(colorGroup, out var requiredCount))
        {
            return false;
        }

        var ownedCount = state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Property &&
            string.Equals(cell.ColorGroup, colorGroup, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal));

        return ownedCount >= requiredCount;
    }

    private static bool HasMortgagedInColorSet(MonopolyRoomState state, string playerId, string? colorGroup)
    {
        if (string.IsNullOrWhiteSpace(colorGroup))
        {
            return false;
        }

        return state.Cells.Any(cell =>
            cell.Type == MonopolyCellType.Property &&
            string.Equals(cell.ColorGroup, colorGroup, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal) &&
            cell.IsMortgaged);
    }

    private static bool CanBuildEvenly(MonopolyRoomState state, string playerId, MonopolyCellState target)
    {
        var groupCells = GetOwnedColorGroupCells(state, playerId, target.ColorGroup);
        if (groupCells.Count == 0)
        {
            return false;
        }

        var targetLevel = target.BuildingLevel;
        var minLevel = groupCells.Min(cell => cell.BuildingLevel);
        return targetLevel == minLevel;
    }

    private static bool CanSellEvenly(MonopolyRoomState state, string playerId, MonopolyCellState target)
    {
        var groupCells = GetOwnedColorGroupCells(state, playerId, target.ColorGroup);
        if (groupCells.Count == 0)
        {
            return false;
        }

        var targetLevel = target.BuildingLevel;
        var maxLevel = groupCells.Max(cell => cell.BuildingLevel);
        return targetLevel == maxLevel;
    }

    private static List<MonopolyCellState> GetOwnedColorGroupCells(
        MonopolyRoomState state,
        string playerId,
        string? colorGroup)
    {
        if (string.IsNullOrWhiteSpace(colorGroup))
        {
            return [];
        }

        return state.Cells
            .Where(cell =>
                cell.Type == MonopolyCellType.Property &&
                string.Equals(cell.ColorGroup, colorGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal))
            .ToList();
    }

    private static bool ValidateTradeCells(
        MonopolyRoomState state,
        string ownerPlayerId,
        IReadOnlyList<int> cellIds,
        out string? error)
    {
        foreach (var cellId in cellIds)
        {
            var cell = state.FindCell(cellId);
            if (cell is null)
            {
                error = $"ไม่พบทรัพย์สินช่อง {cellId}";
                return false;
            }

            if (!string.Equals(cell.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
            {
                error = $"ช่อง {cellId} ไม่ได้เป็นของผู้เล่นเจ้าของข้อเสนอ";
                return false;
            }

            if (cell.HouseCount > 0 || cell.HasHotel)
            {
                error = $"ช่อง {cellId} มีสิ่งปลูกสร้างอยู่ ยังไม่อนุญาตให้เทรดในเวอร์ชันนี้";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static List<int> DistinctCells(IReadOnlyList<int>? cells)
    {
        return (cells ?? Array.Empty<int>())
            .Where(cell => cell > 0)
            .Distinct()
            .OrderBy(cell => cell)
            .ToList();
    }

    private static IEnumerable<PlayerState> ResolveAuctionTurnOrder(GameRoom room, string? activePlayerId)
    {
        if (room.Players.Count == 0)
        {
            yield break;
        }

        var startIndex = 0;
        if (!string.IsNullOrWhiteSpace(activePlayerId))
        {
            var idx = room.Players.FindIndex(player =>
                string.Equals(player.PlayerId, activePlayerId, StringComparison.Ordinal));
            if (idx >= 0)
            {
                startIndex = idx;
            }
        }

        for (var i = 0; i < room.Players.Count; i++)
        {
            yield return room.Players[(startIndex + i) % room.Players.Count];
        }
    }

    private static int MortgageValue(MonopolyCellState cell)
    {
        return Math.Max(0, (int)Math.Floor(cell.Price / 2d));
    }

    private static int UnmortgageCost(MonopolyCellState cell)
    {
        var mortgage = MortgageValue(cell);
        return (int)Math.Ceiling(mortgage * 1.1d);
    }

    private static int GetAndIncrement(Dictionary<string, int> map, string playerId)
    {
        map.TryGetValue(playerId, out var current);
        var next = current + 1;
        map[playerId] = next;
        return next;
    }

    private static int ResetAndGet(Dictionary<string, int> map, string playerId)
    {
        map[playerId] = 0;
        return 0;
    }

    private static void ResetPlayerForMonopoly(PlayerState player, string hostPlayerId, bool waitingMode)
    {
        player.Position = 1;
        player.Shields = 0;
        player.ConsecutiveSnakeHits = 0;
        player.MercyLadderPending = false;
        player.SnakeRepellentCharges = 0;
        player.LadderHackPending = false;
        player.AnchorTurnsRemaining = 0;
        player.ItemDryTurnStreak = 0;
        player.NextCheckpoint = 50;
        player.LuckyRerollsLeft = 0;
        player.Cash = MonopolyDefinitions.DefaultStartCash;
        player.IsBankrupt = false;
        player.JailTurnsRemaining = 0;
        if (waitingMode)
        {
            player.IsReady = player.PlayerId == hostPlayerId;
        }
    }

    private sealed class ActionExecution
    {
        public ActionExecution(int startPosition)
        {
            StartPosition = startPosition;
        }

        public int StartPosition { get; }
        public int DiceOne { get; set; }
        public int DiceTwo { get; set; }
        public bool ExtraTurnGranted { get; set; }
        public string? AutoReason { get; set; }
        public List<string> Logs { get; } = [];
    }
}
