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
            monopoly.JailFineByPlayer[player.PlayerId] = MonopolyDefinitions.JailFine;
            monopoly.ExtraTurnByPlayer[player.PlayerId] = false;
        }

        room.Status = GameStatus.Started;
        room.CurrentTurnIndex = random.Next(0, room.Players.Count);
        room.TurnCounter = 0;
        room.CompletedRounds = 0;
        monopoly.CityPriceGrowthRounds = 0;
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
        execution.Logs.Add(isDouble ? "ผลการทอย: ดับเบิ้ล" : "ผลการทอย: ไม่ดับเบิ้ล");

        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.Resolving;

        if (isDouble && currentDoubleCount >= 3)
        {
            SendPlayerToJail(state, actor, execution.Logs, "ทอยดับเบิล 3 ครั้งติด ถูกส่งเข้าคุก");
            state.ExtraTurnByPlayer[actor.PlayerId] = false;
            return null;
        }

        MovePlayerBy(room, state, actor, d1 + d2, execution.Logs);

        if (state.Phase == MonopolyTurnPhase.Resolving)
        {
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = actor.PlayerId;
        }

        var canReceiveExtraTurn = isDouble &&
                                  !actor.IsBankrupt &&
                                  actor.JailTurnsRemaining <= 0;
        state.ExtraTurnByPlayer[actor.PlayerId] = canReceiveExtraTurn;
        execution.ExtraTurnGranted = canReceiveExtraTurn;
        if (canReceiveExtraTurn)
        {
            execution.Logs.Add("ได้สิทธิ์เล่นต่ออีกตา (ทอยดับเบิล)");
        }

        return null;
    }

    private static string? HandlePayJailFine(
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

        var currentFine = CurrentJailFine(state, actor.PlayerId);
        if (currentFine <= 0)
        {
            ResetJailState(state, actor);
            state.Phase = MonopolyTurnPhase.AwaitRoll;
            state.PendingDecisionPlayerId = actor.PlayerId;
            execution.Logs.Add("ออกจากคุกแล้ว");
            return null;
        }

        ChargePlayer(room, state, actor, null, currentFine, execution.Logs, "ค่าประกันออกจากคุก");
        if (state.PendingDebtAmount > 0 || actor.Cash < 0)
        {
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = actor.PlayerId;
            execution.Logs.Add($"ต้องขายทรัพย์สินเพื่อหาเงินชำระค่าประกันออกจากคุกอีก ฿{state.PendingDebtAmount}");
            return null;
        }

        ResetJailState(state, actor);
        state.Phase = MonopolyTurnPhase.AwaitRoll;
        state.PendingDecisionPlayerId = actor.PlayerId;
        execution.Logs.Add($"จ่ายค่าประกัน ฿{currentFine} และออกจากคุกแล้ว");
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
        execution.Logs.Add(isDouble ? "ผลการทอย: ดับเบิ้ล" : "ผลการทอย: ไม่ดับเบิ้ล");

        ClearTransientActionState(state, keepDebt: true);

        if (isDouble)
        {
            ResetJailState(state, actor);
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

        var nextFine = NextJailFine(CurrentJailFine(state, actor.PlayerId));
        state.JailFineByPlayer[actor.PlayerId] = nextFine;
        actor.JailTurnsRemaining = Math.Max(1, attempts + 1);
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        state.PendingDecisionPlayerId = actor.PlayerId;
        state.ExtraTurnByPlayer[actor.PlayerId] = false;
        execution.Logs.Add($"ยังออกจากคุกไม่ได้ ค่าประกันรอบถัดไปเพิ่มเป็น ฿{nextFine}");
        return null;
    }

    private static string? HandleBuyProperty(
        GameRoom room,
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

        var purchasePrice = ResolvePendingPurchasePrice(state, cell, state.CityPriceGrowthRounds);
        var sellerId = state.PendingPurchaseOwnerPlayerId;
        if (!string.IsNullOrWhiteSpace(sellerId))
        {
            if (sellerId == actor.PlayerId)
            {
                return "ไม่สามารถซื้อทรัพย์สินของตัวเองซ้ำได้";
            }

            if (!string.Equals(cell.OwnerPlayerId, sellerId, StringComparison.Ordinal))
            {
                return "ทรัพย์สินนี้ไม่ได้อยู่ในสถานะขายต่อแล้ว";
            }

            if (cell.HasLandmark)
            {
                return "ทรัพย์สินที่มีแลนด์มาร์กไม่สามารถซื้อจากเจ้าของได้";
            }

            if (actor.Cash < purchasePrice)
            {
                return "เงินไม่พอซื้อทรัพย์สินนี้";
            }

            var seller = room.FindPlayer(sellerId);
            if (seller is null || seller.IsBankrupt)
            {
                return "ไม่พบเจ้าของเดิมที่พร้อมรับเงินจากการขาย";
            }

            actor.Cash -= purchasePrice;
            seller.Cash += purchasePrice;
            cell.OwnerPlayerId = actor.PlayerId;
            execution.Logs.Add($"ซื้อ {cell.Name} ต่อจาก {seller.DisplayName} ในราคา ฿{purchasePrice}");
        }

        if (string.IsNullOrWhiteSpace(sellerId))
        {
            if (!string.IsNullOrWhiteSpace(cell.OwnerPlayerId))
            {
                return "ทรัพย์สินนี้ถูกซื้อไปแล้ว";
            }

            if (actor.Cash < purchasePrice)
            {
                return "เงินไม่พอซื้อทรัพย์สินนี้";
            }

            actor.Cash -= purchasePrice;
            cell.OwnerPlayerId = actor.PlayerId;
            cell.IsMortgaged = false;
            cell.HouseCount = 0;
            cell.HasHotel = false;
            cell.HasLandmark = false;
            execution.Logs.Add($"ซื้อ {cell.Name} ในราคา ฿{purchasePrice}");
        }
        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = actor.PlayerId;
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
            return "ไม่พบทรัพย์สินที่รอการตัดสินใจ";
        }

        execution.Logs.Add(
            string.IsNullOrWhiteSpace(state.PendingPurchaseOwnerPlayerId)
                ? $"ข้ามการซื้อ {cell.Name}"
                : $"ไม่ซื้อ {cell.Name} ต่อจากเจ้าของเดิม");
        ClearTransientActionState(state, keepDebt: true);
        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = actor.PlayerId;
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

        if (state.UpgradeUsedThisTurn)
        {
            return "เทิร์นนี้ใช้อัปเกรดไปแล้ว";
        }

        var cell = ResolveOwnedBuildCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบทรัพย์สินที่สามารถสร้างสิ่งปลูกสร้างได้";
        }

        if (!state.UpgradeEligibleCellIds.Contains(cell.Cell))
        {
            return "ตอนนี้ยังไม่มีสิทธิ์อัปเกรดช่องนี้";
        }

        if (cell.IsMortgaged)
        {
            return "ช่องที่ติดจำนองยังอัปเกรดไม่ได้";
        }

        if (cell.HasLandmark)
        {
            return "แปลงนี้มีแลนด์มาร์กอยู่แล้ว";
        }

        if (cell.HouseCount < 4)
        {
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
            ConsumeTurnUpgrade(state, cell.Cell);
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            execution.Logs.Add($"สร้างบ้านที่ {cell.Name} (+1 บ้าน)");
            return null;
        }

        if (cell.HasHotel)
        {
            var landmarkCost = LandmarkCost(cell);
            if (actor.Cash < landmarkCost)
            {
                return "เงินไม่พอสร้างแลนด์มาร์ก";
            }

            actor.Cash -= landmarkCost;
            cell.HasLandmark = true;
            ConsumeTurnUpgrade(state, cell.Cell);
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            execution.Logs.Add($"สร้างแลนด์มาร์กที่ {cell.Name}");
            return null;
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
        ConsumeTurnUpgrade(state, cell.Cell);
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

        if (cell.HasLandmark)
        {
            cell.HasLandmark = false;
            actor.Cash += Math.Max(1, LandmarkCost(cell) / 2);
            state.Phase = MonopolyTurnPhase.AwaitEndTurn;
            execution.Logs.Add($"ขายแลนด์มาร์กที่ {cell.Name}");
            return null;
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

    private static string? HandleSellProperty(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState actor,
        MonopolyActionPayload? payload,
        ActionExecution execution)
    {
        if (state.Phase is not MonopolyTurnPhase.AwaitManage and not MonopolyTurnPhase.AwaitEndTurn)
        {
            return "ยังไม่ถึงช่วงจัดการทรัพย์สิน";
        }

        if (state.PendingDebtAmount <= 0 && actor.Cash >= 0)
        {
            return "ตอนนี้ยังไม่จำเป็นต้องขายอสังหา";
        }

        var cell = ResolveOwnedAssetCell(state, actor.PlayerId, payload?.CellId);
        if (cell is null)
        {
            return "ไม่พบอสังหาที่สามารถขายได้";
        }

        var creditor = !string.IsNullOrWhiteSpace(state.PendingDebtToPlayerId)
            ? room.FindPlayer(state.PendingDebtToPlayerId)
            : null;

        if (creditor is not null && !creditor.IsBankrupt)
        {
            if (cell.HasLandmark)
            {
                return "อสังหาที่มีแลนด์มาร์กยังขายโอนให้ผู้เล่นอื่นไม่ได้ ต้องรื้อแลนด์มาร์กก่อน";
            }

            var saleValue = CalculateTakeoverPrice(cell, state.CityPriceGrowthRounds);
            var debtAmount = Math.Max(0, state.PendingDebtAmount);
            var surplus = Math.Max(0, saleValue - debtAmount);
            if (surplus > 0 && creditor.Cash < surplus)
            {
                return $"เจ้าหนี้มีเงินไม่พอรับซื้อทรัพย์นี้ (ต้องมีอย่างน้อย ฿{surplus})";
            }

            if (surplus > 0)
            {
                creditor.Cash -= surplus;
            }

            actor.Cash += saleValue;
            cell.OwnerPlayerId = creditor.PlayerId;
            execution.Logs.Add($"ขาย {cell.Name} ให้ {creditor.DisplayName} มูลค่า ฿{saleValue}");
        }
        else
        {
            var saleValue = CalculateBankLiquidationValue(cell, state.CityPriceGrowthRounds);
            actor.Cash += saleValue;
            ResetCellToBank(cell);
            execution.Logs.Add($"ขาย {cell.Name} คืนธนาคาร มูลค่า ฿{saleValue}");
        }

        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = actor.PlayerId;
        return null;
    }

    private static string? HandleMortgage(
        GameRoom room,
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

        if (cell.HouseCount > 0 || cell.HasHotel || cell.HasLandmark)
        {
            return "ต้องขายสิ่งปลูกสร้างก่อนจำนอง";
        }

        var value = MortgageValue(cell, state.CityPriceGrowthRounds);
        cell.IsMortgaged = true;
        actor.Cash += value;
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        execution.Logs.Add($"จำนอง {cell.Name} รับเงิน ฿{value}");
        return null;
    }

    private static string? HandleUnmortgage(
        GameRoom room,
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

        var cost = UnmortgageCost(cell, state.CityPriceGrowthRounds);
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

        if (actor.Cash < 0 || state.PendingDebtAmount > 0)
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
            ClearTurnUpgradeState(state);
            state.ActivePlayerId = actor.PlayerId;
            state.PendingDecisionPlayerId = actor.PlayerId;
            state.Phase = actor.JailTurnsRemaining > 0
                ? MonopolyTurnPhase.AwaitJailDecision
                : MonopolyTurnPhase.AwaitRoll;
            execution.ExtraTurnGranted = true;
            execution.Logs.Add("จบเทิร์นและได้เล่นต่ออีกตา");
            return null;
        }

        AdvanceTurn(room, state, execution.Logs);
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

        AwardPassGoCash(player, CountPassGoEvents(start, Math.Max(0, steps), boardSize), logs, "ผ่าน GO");

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

        ClearTurnUpgradeEligibility(state);

        var landing = state.FindCell(player.Position);
        if (landing is null)
        {
            logs.Add($"เดินถึงช่อง {player.Position}");
            return;
        }

        switch (landing.Type)
        {
            case MonopolyCellType.Go:
                GrantGoUpgradeOpportunity(state, player, logs);
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

            var currentCityPrice = CalculateCityPrice(landing, state.CityPriceGrowthRounds);
            state.PendingPurchaseCellId = landing.Cell;
            state.PendingPurchasePrice = currentCityPrice;
            state.PendingPurchaseOwnerPlayerId = null;
            state.PendingDecisionPlayerId = player.PlayerId;
            state.Phase = MonopolyTurnPhase.AwaitPurchaseDecision;
            if (player.Cash < currentCityPrice)
            {
                var shortfall = currentCityPrice - player.Cash;
                logs.Add(
                    $"{landing.Name} ยังไม่มีเจ้าของ: เงินคุณไม่พอซื้อ (ขาด ฿{shortfall}) กดข้ามการซื้อแล้วไปจัดการทรัพย์สินต่อได้");
            }
            else
            {
                logs.Add($"{landing.Name} ยังไม่มีเจ้าของ: ซื้อได้ทันทีในราคา ฿{currentCityPrice} หรือกดข้ามการซื้อ");
            }
            return;
        }

        if (string.Equals(landing.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal))
        {
            var upgradeOpportunity = DescribeImmediateUpgradeOpportunity(state, player, landing);
            logs.Add(
                upgradeOpportunity is null
                    ? $"ถึง {landing.Name} (ทรัพย์สินของคุณ)"
                    : $"ถึง {landing.Name} (เมืองของคุณ) {upgradeOpportunity}");
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

        var baseToll = CalculateBaseRent(room, state, landing, owner.PlayerId);
        var neighborhoodBonus = CalculateNeighborhoodSurcharge(state, landing, owner.PlayerId, baseToll);
        var toll = baseToll + neighborhoodBonus;
        if (toll <= 0)
        {
            logs.Add($"ถึง {landing.Name}");
            return;
        }

        logs.Add(
            neighborhoodBonus > 0
                ? $"{landing.Name}: ค่าผ่านทางพื้นฐาน ฿{baseToll} + โบนัสละแวก ฿{neighborhoodBonus} = ฿{toll}"
                : $"{landing.Name}: ค่าผ่านทาง ฿{toll}");
        ChargePlayer(room, state, player, owner, toll, logs, $"ค่าผ่านทาง {landing.Name}");

        if (state.PendingDebtAmount > 0 || player.Cash < 0)
        {
            state.Phase = MonopolyTurnPhase.AwaitManage;
            state.PendingDecisionPlayerId = player.PlayerId;
            return;
        }

        if (CanOfferTakeover(landing))
        {
            var buyoutPrice = CalculateTakeoverPrice(landing, state.CityPriceGrowthRounds);
            state.PendingPurchaseCellId = landing.Cell;
            state.PendingPurchasePrice = buyoutPrice;
            state.PendingPurchaseOwnerPlayerId = owner.PlayerId;
            state.PendingDecisionPlayerId = player.PlayerId;
            state.Phase = MonopolyTurnPhase.AwaitPurchaseDecision;
            logs.Add(
                player.Cash >= buyoutPrice
                    ? $"จ่ายค่าผ่านทางแล้ว จะซื้อ {landing.Name} ต่อจาก {owner.DisplayName} ในราคา ฿{buyoutPrice} หรือจบเทิร์นก็ได้"
                    : $"จ่ายค่าผ่านทางแล้ว แต่ยังขาด ฿{buyoutPrice - player.Cash} หากอยากซื้อ {landing.Name} ต่อจาก {owner.DisplayName}");
            return;
        }

        state.Phase = MonopolyTurnPhase.AwaitManage;
        state.PendingDecisionPlayerId = player.PlayerId;
        logs.Add($"ทรัพย์สินนี้เป็นแลนด์มาร์กแล้ว จึงซื้อจากเจ้าของต่อไม่ได้");
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
        if (state.PendingDebtAmount > 0)
        {
            state.PendingDebtReason = $"ภาษี {landing.Name}";
        }
        logs.Add($"จ่ายภาษี {landing.Name} จำนวน ฿{fee}");
    }

    private static void ResolveChanceEvent(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        int depth)
    {
        var idx = Math.Abs(state.ChanceCursor++) % 12;

        switch (idx)
        {
            case 0:
                MovePlayerToCell(room, state, player, 1, logs, depth, "โอกาส: ไปช่องเริ่มต้น (GO)");
                return;
            case 1:
                player.Cash += 120;
                logs.Add("โอกาส: รับโบนัสการลงทุน +฿120");
                return;
            case 2:
                ChargeBankDebt(state, player, 120, "โอกาส: โดนค่าปรับจราจร", logs);
                return;
            case 3:
                MovePlayerBack(room, state, player, 3, logs, depth, "โอกาส: ถอยหลัง 3 ช่อง");
                return;
            case 4:
                SendPlayerToJail(state, player, logs, "โอกาส: เข้าคุกทันที");
                return;
            case 5:
                MovePlayerToNearestType(room, state, player, MonopolyCellType.Railroad, logs, depth, "โอกาส: GPS พาไปสถานีรถไฟที่ใกล้ที่สุด");
                return;
            case 6:
                MovePlayerToNearestType(room, state, player, MonopolyCellType.Utility, logs, depth, "โอกาส: ไปสาธารณูปโภคที่ใกล้ที่สุด");
                return;
            case 7:
                MovePlayerToCell(room, state, player, 38, logs, depth, "โอกาส: พุ่งตรงไปกรุงเทพมหานคร");
                return;
            case 8:
                MovePlayerToCell(room, state, player, 22, logs, depth, "โอกาส: ทริปด่วนสู่ภูเก็ต");
                return;
            case 9:
                CollectFromAllPlayers(room, state, player, 60, logs, "โอกาส: รับเงินจากผู้เล่นทุกคน คนละ ฿60");
                return;
            case 10:
                PayAllPlayers(room, state, player, 60, logs, "โอกาส: จ่ายให้ผู้เล่นทุกคน คนละ ฿60");
                return;
            default:
                ConfiscatePropertyShareToBank(state, player, logs, "โอกาส: โดนเวนคืนทรัพย์ 20% คืนรัฐ", 0.2d);
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
        var idx = Math.Abs(state.CommunityCursor++) % 12;

        switch (idx)
        {
            case 0:
                player.Cash += 180;
                logs.Add("การ์ดชุมชน: ธนาคารคืนภาษี +฿180");
                return;
            case 1:
                player.Cash += 100;
                logs.Add("การ์ดชุมชน: ขายหุ้นได้กำไร +฿100");
                return;
            case 2:
                player.Cash += 80;
                logs.Add("การ์ดชุมชน: รับค่าที่ปรึกษา +฿80");
                return;
            case 3:
                player.Cash += 140;
                logs.Add("การ์ดชุมชน: กองทุนท่องเที่ยวคืนเงิน +฿140");
                return;
            case 4:
                ChargeBankDebt(state, player, 120, "การ์ดชุมชน: จ่ายค่าหมอ", logs);
                return;
            case 5:
                ChargeBankDebt(state, player, 180, "การ์ดชุมชน: จ่ายค่าเล่าเรียน", logs);
                return;
            case 6:
                player.Cash += 150;
                logs.Add("การ์ดชุมชน: ได้เงินสนับสนุนจากรัฐ +฿150");
                return;
            case 7:
                ChargeOwnedPropertyFee(state, player, 50, logs, "การ์ดชุมชน: จ่ายค่าบำรุงเมือง");
                return;
            case 8:
                CreditByOwnedAssets(state, player, 30, logs, "การ์ดชุมชน: รายได้ค่าเช่าสะสม");
                return;
            case 9:
                ChargeByMortgagedAssets(state, player, 90, logs, "การ์ดชุมชน: เมืองของคุณโดนตรวจภาษี");
                return;
            case 10:
                CollectFromAllPlayers(room, state, player, 50, logs, "การ์ดชุมชน: ทุกคนช่วยออกค่าจัดงานให้คุณ คนละ ฿50");
                return;
            default:
                PayAllPlayers(room, state, player, 50, logs, "การ์ดชุมชน: คุณต้องเลี้ยงทีมงาน จ่ายทุกคน คนละ ฿50");
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

        logs.Add(reason);
        AwardPassGoCash(player, target < current ? 1 : 0, logs, "ผ่าน GO");

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
                cell.HasLandmark = false;
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

    private static int CalculateBaseRent(
        GameRoom room,
        MonopolyRoomState state,
        MonopolyCellState landing,
        string ownerPlayerId)
    {
        if (landing.IsMortgaged)
        {
            return 0;
        }

        var baseAmount = landing.Type switch
        {
            MonopolyCellType.Property => CalculatePropertyRent(state, landing, ownerPlayerId),
            MonopolyCellType.Railroad => CalculateRailroadRent(state, ownerPlayerId),
            MonopolyCellType.Utility => CalculateUtilityRent(state, ownerPlayerId),
            _ => Math.Max(0, landing.Rent)
        };

        if (baseAmount <= 0)
        {
            return 0;
        }

        return ApplyRentScaling(baseAmount, room.CompletedRounds);
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

        if (landing.HasLandmark)
        {
            return baseRent * MonopolyDefinitions.LandmarkRentMultiplier;
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

        return 40 * (int)Math.Pow(2, count - 1);
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
        return (count >= 2 ? 14 : 6) * diceTotal;
    }

    private static int CalculateNeighborhoodSurcharge(
        MonopolyRoomState state,
        MonopolyCellState landing,
        string ownerPlayerId,
        int baseAmount)
    {
        if (baseAmount <= 0)
        {
            return 0;
        }

        var radius = Math.Max(1, MonopolyDefinitions.NeighborhoodRadius);
        var bonus = 0d;
        foreach (var candidate in state.Cells)
        {
            if (candidate.Cell == landing.Cell ||
                string.IsNullOrWhiteSpace(candidate.OwnerPlayerId) ||
                !string.Equals(candidate.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = BoardDistance(landing.Cell, candidate.Cell, MonopolyDefinitions.DefaultBoardCellCount);
            if (distance <= 0 || distance > radius)
            {
                continue;
            }

            bonus += ResolveNeighborhoodWeight(candidate) * (
                distance == 1
                    ? MonopolyDefinitions.NeighborhoodPrimaryBonus
                    : MonopolyDefinitions.NeighborhoodSecondaryBonus);
        }

        return Math.Max(0, (int)Math.Ceiling(baseAmount * bonus));
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
                state.PendingDebtReason = reason;
                logs.Add($"{debtor.DisplayName} จ่าย {reason} ให้ {creditor.DisplayName} ได้บางส่วน ฿{paidNow} (ค้าง ฿{remainingDebt})");
            }
            else
            {
                state.PendingDebtToPlayerId = null;
                state.PendingDebtAmount = 0;
                state.PendingDebtReason = null;
                logs.Add($"{debtor.DisplayName} จ่าย {reason} ให้ {creditor.DisplayName} จำนวน ฿{charge}");
            }

            return;
        }

        if (debtor.Cash < 0)
        {
            state.PendingDebtToPlayerId = null;
            state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Abs(debtor.Cash));
            state.PendingDebtReason = reason;
        }
        else if (state.PendingDebtToPlayerId is null)
        {
            state.PendingDebtReason = null;
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
                state.PendingDebtReason = null;
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
            state.PendingDebtReason = null;
        }
    }

    private static void TryRestoreRollAfterJailFineDebt(
        MonopolyRoomState state,
        PlayerState actor,
        string? pendingDebtReasonBeforeSettle,
        List<string> logs)
    {
        if (!string.Equals(pendingDebtReasonBeforeSettle, "ค่าประกันออกจากคุก", StringComparison.Ordinal) ||
            actor.IsBankrupt ||
            actor.JailTurnsRemaining <= 0 ||
            actor.Cash < 0 ||
            state.PendingDebtAmount > 0)
        {
            return;
        }

        ResetJailState(state, actor);
        state.ActivePlayerId = actor.PlayerId;
        state.PendingDecisionPlayerId = actor.PlayerId;
        state.Phase = MonopolyTurnPhase.AwaitRoll;
        logs.Add("หาเงินจ่ายค่าประกันครบแล้ว ออกจากคุกและพร้อมทอยต่อ");
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
        player.EliminationReason = $"แพ้เพราะล้มละลาย ({reason})";

        foreach (var cell in state.Cells.Where(x => string.Equals(x.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal)))
        {
            if (creditor is not null && !creditor.IsBankrupt)
            {
                cell.OwnerPlayerId = creditor.PlayerId;
            }
            else
            {
                RestoreSupplyFromCell(state, cell);
                cell.OwnerPlayerId = null;
                cell.IsMortgaged = false;
                cell.HouseCount = 0;
                cell.HasHotel = false;
                cell.HasLandmark = false;
            }
        }

        state.ConsecutiveDoublesByPlayer[player.PlayerId] = 0;
        state.JailAttemptByPlayer[player.PlayerId] = 0;
        state.JailFineByPlayer[player.PlayerId] = MonopolyDefinitions.JailFine;
        state.ExtraTurnByPlayer[player.PlayerId] = false;

        state.PendingDebtToPlayerId = null;
        state.PendingDebtAmount = 0;
        state.PendingDebtReason = null;

        logs.Add($"{player.DisplayName} ล้มละลาย ({reason})");
    }

    private static void SendPlayerToJail(
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        string reason)
    {
        player.Position = MonopolyDefinitions.JailCell;
        player.JailTurnsRemaining = 1;
        ResetJailState(state, player);
        player.JailTurnsRemaining = 1;
        state.ConsecutiveDoublesByPlayer[player.PlayerId] = 0;
        state.ExtraTurnByPlayer[player.PlayerId] = false;
        state.PendingPurchaseCellId = null;
        state.PendingPurchasePrice = 0;
        state.PendingPurchaseOwnerPlayerId = null;
        state.ActiveAuction = null;
        state.ActiveTradeOffer = null;
        ClearTurnUpgradeState(state);
        state.Phase = MonopolyTurnPhase.AwaitEndTurn;
        state.PendingDecisionPlayerId = player.PlayerId;
        logs.Add(reason);
    }

    private static void AdvanceTurn(GameRoom room, MonopolyRoomState state, List<string>? logs = null)
    {
        ClearTransientActionState(state, keepDebt: false);
        ClearTurnUpgradeState(state);

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
                logs?.Add($"เศรษฐกิจเมือง: ค่าผ่านทางทุกแปลงเพิ่มเป็น +{Math.Round(room.CompletedRounds * MonopolyDefinitions.RentGrowthPerCompletedRound * 100)}%");
                if (ShouldAdvanceCityPriceEconomy(state))
                {
                    state.CityPriceGrowthRounds++;
                    logs?.Add($"ตลาดเมือง: ราคาเมืองทุกแปลงเพิ่มเป็น +{Math.Round(state.CityPriceGrowthRounds * MonopolyDefinitions.CityPriceGrowthPerCompletedRound * 100)}%");
                }
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
        state.PendingPurchasePrice = 0;
        state.PendingPurchaseOwnerPlayerId = null;
        state.ActiveAuction = null;
        state.ActiveTradeOffer = null;

        if (!keepDebt)
        {
            state.PendingDebtAmount = 0;
            state.PendingDebtToPlayerId = null;
            state.PendingDebtReason = null;
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
                .OrderByDescending(player => CalculateNetWorth(state, player.PlayerId, player.Cash, state.CityPriceGrowthRounds))
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

    private static int CalculateNetWorth(MonopolyRoomState state, string playerId, int cash, int cityPriceGrowthRounds)
    {
        var assetValue = 0;
        foreach (var cell in state.Cells.Where(cell =>
                     string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal)))
        {
            var baseValue = cell.IsMortgaged
                ? MortgageValue(cell, cityPriceGrowthRounds)
                : CalculateCityPrice(cell, cityPriceGrowthRounds);
            var buildingValue = cell.HasLandmark
                ? cell.HouseCost * 7
                : cell.HasHotel
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

            if (cell.HouseCount > 0 || cell.HasHotel || cell.HasLandmark)
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

    private static string? DescribeImmediateUpgradeOpportunity(
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState landing)
    {
        if (!CanUpgradePropertyNow(state, player, landing))
        {
            return null;
        }

        state.UpgradeEligibleCellIds.Clear();
        state.UpgradeEligibleCellIds.Add(landing.Cell);

        if (landing.HasHotel)
        {
            var cost = LandmarkCost(landing);
            return player.Cash >= cost
                ? $"สามารถสร้างแลนด์มาร์กได้ทันที (ใช้ ฿{cost})"
                : $"พร้อมสร้างแลนด์มาร์กแล้ว แต่ยังขาดอีก ฿{cost - player.Cash}";
        }

        if (landing.HouseCount < 4)
        {
            if (state.AvailableHouses <= 0)
            {
                return "ถือชุดสีครบแล้ว แต่บ้านในธนาคารหมด";
            }

            var cost = Math.Max(0, landing.HouseCost);
            return player.Cash >= cost
                ? $"สามารถสร้างบ้านหลังที่ {landing.HouseCount + 1} ได้ทันที (ใช้ ฿{cost})"
                : $"ถือชุดสีครบแล้ว แต่ยังขาดอีก ฿{cost - player.Cash} สำหรับบ้านหลังถัดไป";
        }

        if (state.AvailableHotels <= 0)
        {
            return "ถือชุดสีครบแล้ว แต่โรงแรมในธนาคารหมด";
        }

        var hotelCost = Math.Max(0, landing.HouseCost);
        return player.Cash >= hotelCost
            ? $"สามารถอัปเกรดเป็นโรงแรมได้ทันที (ใช้ ฿{hotelCost})"
            : $"พร้อมอัปเกรดเป็นโรงแรมแล้ว แต่ยังขาดอีก ฿{hotelCost - player.Cash}";
    }

    private static bool CanUpgradePropertyNow(
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState cell)
    {
        if (state.UpgradeUsedThisTurn ||
            cell.Type != MonopolyCellType.Property ||
            !string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) ||
            cell.IsMortgaged ||
            cell.HasLandmark)
        {
            return false;
        }

        if (cell.HasHotel)
        {
            return player.Cash >= LandmarkCost(cell);
        }

        if (cell.HouseCount < 4)
        {
            return state.AvailableHouses > 0 && player.Cash >= cell.HouseCost;
        }

        return state.AvailableHotels > 0 && player.Cash >= cell.HouseCost;
    }

    private static void GrantGoUpgradeOpportunity(
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs)
    {
        if (state.UpgradeUsedThisTurn)
        {
            return;
        }

        var eligible = state.Cells
            .Where(cell => CanUpgradePropertyNow(state, player, cell))
            .Select(cell => cell.Cell)
            .OrderBy(cell => cell)
            .ToArray();

        if (eligible.Length == 0)
        {
            return;
        }

        state.UpgradeEligibleCellIds.Clear();
        state.UpgradeEligibleCellIds.AddRange(eligible);
        logs.Add($"ถึง GO และมีสิทธิ์เลือกอัปเกรดเมืองของคุณได้ 1 เมือง ({eligible.Length} ตัวเลือก)");
    }

    private static void ClearTurnUpgradeEligibility(MonopolyRoomState state)
    {
        state.UpgradeEligibleCellIds.Clear();
    }

    private static void ClearTurnUpgradeState(MonopolyRoomState state)
    {
        state.UpgradeUsedThisTurn = false;
        state.UpgradeEligibleCellIds.Clear();
    }

    private static void ConsumeTurnUpgrade(MonopolyRoomState state, int cellId)
    {
        state.UpgradeUsedThisTurn = true;
        state.UpgradeEligibleCellIds.Clear();
        state.UpgradeEligibleCellIds.Add(cellId);
    }

    private static int ResolvePendingPurchasePrice(MonopolyRoomState state, MonopolyCellState cell, int completedRounds)
    {
        if (state.PendingPurchasePrice > 0)
        {
            return state.PendingPurchasePrice;
        }

        return string.IsNullOrWhiteSpace(state.PendingPurchaseOwnerPlayerId)
            ? CalculateCityPrice(cell, completedRounds)
            : CalculateTakeoverPrice(cell, completedRounds);
    }

    private static bool CanOfferTakeover(MonopolyCellState cell)
    {
        return cell.Type is MonopolyCellType.Property or MonopolyCellType.Railroad or MonopolyCellType.Utility &&
               !cell.HasLandmark;
    }

    private static int CalculateTakeoverPrice(MonopolyCellState cell, int completedRounds)
    {
        var baseValue = cell.IsMortgaged
            ? MortgageValue(cell, completedRounds)
            : CalculateCityPrice(cell, completedRounds);
        var buildingValue = cell.HasLandmark
            ? cell.HouseCost * 7
            : cell.HasHotel
                ? cell.HouseCost * 5
                : Math.Max(0, cell.HouseCount) * cell.HouseCost;
        return Math.Max(1, baseValue + buildingValue);
    }

    private static int CalculateBankLiquidationValue(MonopolyCellState cell, int completedRounds)
    {
        var baseValue = cell.IsMortgaged
            ? MortgageValue(cell, completedRounds)
            : Math.Max(1, (int)Math.Floor(CalculateCityPrice(cell, completedRounds) * MonopolyDefinitions.BankLiquidationBaseRatio));
        var buildingRefund = cell.HasLandmark
            ? cell.HouseCost * 4
            : cell.HasHotel
                ? (int)Math.Ceiling(cell.HouseCost * 2.5d)
                : (int)Math.Ceiling(Math.Max(0, cell.HouseCount) * cell.HouseCost * 0.5d);
        return Math.Max(1, baseValue + buildingRefund);
    }

    private static void RestoreSupplyFromCell(MonopolyRoomState state, MonopolyCellState cell)
    {
        if (cell.HasLandmark)
        {
            return;
        }

        if (cell.HasHotel)
        {
            state.AvailableHotels++;
            state.AvailableHouses += 4;
            return;
        }

        if (cell.HouseCount > 0)
        {
            state.AvailableHouses += Math.Max(0, cell.HouseCount);
        }
    }

    private static void ResetCellToBank(MonopolyCellState cell)
    {
        cell.OwnerPlayerId = null;
        cell.IsMortgaged = false;
        cell.HouseCount = 0;
        cell.HasHotel = false;
        cell.HasLandmark = false;
    }

    private static double ResolveNeighborhoodWeight(MonopolyCellState cell)
    {
        if (cell.HasLandmark)
        {
            return 2.4d;
        }

        if (cell.HasHotel)
        {
            return 1.6d;
        }

        if (cell.HouseCount > 0)
        {
            return 0.75d + (cell.HouseCount * 0.22d);
        }

        return cell.Type switch
        {
            MonopolyCellType.Railroad => 1.1d,
            MonopolyCellType.Utility => 0.9d,
            _ => 0.7d
        };
    }

    private static int BoardDistance(int fromCell, int toCell, int boardSize)
    {
        var normalizedBoard = Math.Max(1, boardSize);
        var direct = Math.Abs(fromCell - toCell);
        return Math.Min(direct, normalizedBoard - direct);
    }

    private static int ApplyRentScaling(int amount, int completedRounds)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var accelerated = amount * MonopolyDefinitions.RentAccelerationMultiplier;
        var growthMultiplier = 1d + (Math.Max(0, completedRounds) * MonopolyDefinitions.RentGrowthPerCompletedRound);
        return Math.Max(1, (int)Math.Ceiling(accelerated * growthMultiplier));
    }

    private static int ApplyEconomyPriceScaling(int amount, int growthRounds)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var growthMultiplier = 1d + (Math.Max(0, growthRounds) * MonopolyDefinitions.CityPriceGrowthPerCompletedRound);
        return Math.Max(1, (int)Math.Ceiling(amount * growthMultiplier));
    }

    private static bool ShouldAdvanceCityPriceEconomy(MonopolyRoomState state)
    {
        var pricedAssets = state.Cells
            .Where(cell => cell.Price > 0 &&
                           cell.Type is MonopolyCellType.Property or MonopolyCellType.Railroad or MonopolyCellType.Utility)
            .ToArray();
        if (pricedAssets.Length == 0)
        {
            return false;
        }

        var ownedCount = pricedAssets.Count(cell => !string.IsNullOrWhiteSpace(cell.OwnerPlayerId));
        var ownedRatio = ownedCount / (double)pricedAssets.Length;
        return ownedRatio > MonopolyDefinitions.CityPriceGrowthOwnershipThreshold;
    }

    private static int CountPassGoEvents(int start, int forwardSteps, int boardSize)
    {
        if (forwardSteps <= 0)
        {
            return 0;
        }

        return Math.Max(0, (start - 1 + forwardSteps) / Math.Max(1, boardSize));
    }

    private static void AwardPassGoCash(PlayerState player, int passCount, List<string> logs, string reason)
    {
        if (passCount <= 0)
        {
            return;
        }

        var total = MonopolyDefinitions.PassGoCash * passCount;
        player.Cash += total;
        logs.Add(passCount == 1
            ? $"{reason} รับเงิน ฿{total}"
            : $"{reason} {passCount} รอบ รับเงิน ฿{total}");
    }

    private static void ChargeBankDebt(
        MonopolyRoomState state,
        PlayerState player,
        int amount,
        string reason,
        List<string> logs)
    {
        var charge = Math.Max(0, amount);
        if (charge <= 0)
        {
            return;
        }

        player.Cash -= charge;
        state.PendingDebtToPlayerId = null;
        state.PendingDebtAmount = Math.Max(state.PendingDebtAmount, Math.Max(0, -player.Cash));
        if (state.PendingDebtAmount > 0)
        {
            state.PendingDebtReason = reason;
        }
        else if (state.PendingDebtToPlayerId is null)
        {
            state.PendingDebtReason = null;
        }

        logs.Add($"{reason} -฿{charge}");
    }

    private static void MovePlayerToNearestType(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellType cellType,
        List<string> logs,
        int depth,
        string reason)
    {
        var boardSize = MonopolyDefinitions.DefaultBoardCellCount;
        var current = Math.Clamp(player.Position, 1, boardSize);
        for (var offset = 1; offset <= boardSize; offset++)
        {
            var target = ((current - 1 + offset) % boardSize) + 1;
            var cell = state.FindCell(target);
            if (cell?.Type == cellType)
            {
                MovePlayerToCell(room, state, player, target, logs, depth, reason);
                return;
            }
        }

        logs.Add(reason);
    }

    private static void CollectFromAllPlayers(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState beneficiary,
        int amountPerPlayer,
        List<string> logs,
        string reason)
    {
        var totalCollected = 0;
        foreach (var player in room.Players.Where(candidate =>
                     !candidate.IsBankrupt &&
                     !string.Equals(candidate.PlayerId, beneficiary.PlayerId, StringComparison.Ordinal)))
        {
            totalCollected += ForceTransferBetweenPlayers(room, state, player, beneficiary, amountPerPlayer, reason, logs);
        }

        logs.Add($"{reason} (รับรวม ฿{totalCollected})");
    }

    private static void PayAllPlayers(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState payer,
        int amountPerPlayer,
        List<string> logs,
        string reason)
    {
        var totalPaid = 0;
        foreach (var player in room.Players.Where(candidate =>
                     !candidate.IsBankrupt &&
                     !string.Equals(candidate.PlayerId, payer.PlayerId, StringComparison.Ordinal)))
        {
            if (payer.IsBankrupt)
            {
                break;
            }

            totalPaid += ForceTransferBetweenPlayers(room, state, payer, player, amountPerPlayer, reason, logs);
        }

        logs.Add($"{reason} (จ่ายรวม ฿{totalPaid})");
    }

    private static int ForceTransferBetweenPlayers(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState fromPlayer,
        PlayerState toPlayer,
        int amount,
        string reason,
        List<string> logs)
    {
        var charge = Math.Max(0, amount);
        if (charge <= 0 || fromPlayer.IsBankrupt || toPlayer.IsBankrupt)
        {
            return 0;
        }

        var paid = Math.Min(Math.Max(0, fromPlayer.Cash), charge);
        fromPlayer.Cash -= charge;
        toPlayer.Cash += paid;

        if (fromPlayer.Cash < 0)
        {
            state.PendingDebtToPlayerId = toPlayer.PlayerId;
            state.PendingDebtAmount = Math.Abs(fromPlayer.Cash);
            state.PendingDebtReason = reason;
            ApplyBankruptcy(room, state, fromPlayer, logs, $"{reason} ให้ {toPlayer.DisplayName}");
        }

        return paid;
    }

    private static void CreditByOwnedAssets(
        MonopolyRoomState state,
        PlayerState player,
        int amountPerAsset,
        List<string> logs,
        string reason)
    {
        var assetCount = state.Cells.Count(cell =>
            string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal));
        var total = Math.Max(0, assetCount * amountPerAsset);
        player.Cash += total;
        logs.Add($"{reason} +฿{total}");
    }

    private static void ChargeOwnedPropertyFee(
        MonopolyRoomState state,
        PlayerState player,
        int amountPerAsset,
        List<string> logs,
        string reason)
    {
        var assetCount = state.Cells.Count(cell =>
            string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal));
        ChargeBankDebt(state, player, assetCount * amountPerAsset, reason, logs);
    }

    private static void ChargeByMortgagedAssets(
        MonopolyRoomState state,
        PlayerState player,
        int amountPerAsset,
        List<string> logs,
        string reason)
    {
        var mortgagedCount = state.Cells.Count(cell =>
            string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
            cell.IsMortgaged);
        ChargeBankDebt(state, player, mortgagedCount * amountPerAsset, reason, logs);
    }

    private static void ConfiscatePropertyShareToBank(
        MonopolyRoomState state,
        PlayerState player,
        List<string> logs,
        string reason,
        double ratio)
    {
        var owned = state.Cells
            .Where(cell => string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal))
            .OrderByDescending(cell => CalculateTakeoverPrice(cell, state.CityPriceGrowthRounds))
            .ThenByDescending(cell => cell.Cell)
            .ToArray();

        if (owned.Length == 0)
        {
            logs.Add($"{reason} แต่คุณไม่มีอสังหาให้ยึด");
            return;
        }

        var confiscateCount = Math.Max(1, (int)Math.Ceiling(owned.Length * Math.Max(0.01d, ratio)));
        var confiscated = owned.Take(confiscateCount).ToArray();
        foreach (var cell in confiscated)
        {
            RestoreSupplyFromCell(state, cell);
            ResetCellToBank(cell);
        }

        logs.Add($"{reason}: {string.Join(", ", confiscated.Select(cell => cell.Name))}");
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

    private static int CalculateCityPrice(MonopolyCellState cell, int growthRounds)
    {
        return ApplyEconomyPriceScaling(Math.Max(0, cell.Price), growthRounds);
    }

    private static int MortgageValue(MonopolyCellState cell, int growthRounds)
    {
        return Math.Max(0, (int)Math.Floor(CalculateCityPrice(cell, growthRounds) / 2d));
    }

    private static int LandmarkCost(MonopolyCellState cell)
    {
        return Math.Max(0, cell.HouseCost * MonopolyDefinitions.LandmarkCostMultiplier);
    }

    private static int UnmortgageCost(MonopolyCellState cell, int growthRounds)
    {
        var mortgage = MortgageValue(cell, growthRounds);
        return (int)Math.Ceiling(mortgage * 1.1d);
    }

    private static int CurrentJailFine(MonopolyRoomState state, string playerId)
    {
        return state.JailFineByPlayer.TryGetValue(playerId, out var fine) && fine > 0
            ? fine
            : MonopolyDefinitions.JailFine;
    }

    private static int NextJailFine(int currentFine)
    {
        var baseFine = Math.Max(1, currentFine);
        return Math.Max(MonopolyDefinitions.JailFine, baseFine * MonopolyDefinitions.JailFineGrowthMultiplier);
    }

    private static void ResetJailState(MonopolyRoomState state, PlayerState player)
    {
        player.JailTurnsRemaining = 0;
        state.JailAttemptByPlayer[player.PlayerId] = 0;
        state.JailFineByPlayer[player.PlayerId] = MonopolyDefinitions.JailFine;
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
        player.EliminationReason = null;
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
