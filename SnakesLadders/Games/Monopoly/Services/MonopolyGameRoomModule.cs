using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class MonopolyGameRoomModule : IGameRoomModule
{
    public string GameKey => GameCatalog.Monopoly;
    public string DisplayName => "เกมเศรษฐี";
    public string Description => "เกมเศรษฐีพื้นฐาน ซื้อที่ดิน จ่ายค่าเช่า เก็บเงินให้รวยสุด";
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
                TurnTimerEnabled = false,
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
        if (room.Board is null || room.Monopoly is null)
        {
            throw new InvalidOperationException("ห้องเกมเศรษฐียังไม่ถูกเตรียมกระดาน");
        }

        var boardSize = MonopolyDefinitions.DefaultBoardCellCount;
        var state = room.Monopoly;
        var startPosition = Math.Clamp(player.Position, 1, boardSize);
        var logs = new List<string>();
        var autoRollReason = isAutoRoll
            ? (player.Connected ? "TimerExpired" : "Disconnected")
            : null;
        var diceValue = 0;
        var endPosition = startPosition;

        if (player.IsBankrupt)
        {
            logs.Add("ผู้เล่นล้มละลายแล้ว ข้ามตานี้");
            room.TurnCounter++;
        }
        else if (player.JailTurnsRemaining > 0)
        {
            player.JailTurnsRemaining--;
            logs.Add($"ติดคุกอยู่ ข้ามตานี้ (เหลืออีก {player.JailTurnsRemaining} ตา)");
            room.TurnCounter++;
        }
        else
        {
            diceValue = Random.Shared.Next(1, 7);
            var rawTarget = startPosition + diceValue;
            var passedGo = rawTarget > boardSize;
            endPosition = ((rawTarget - 1) % boardSize) + 1;

            if (passedGo)
            {
                player.Cash += MonopolyDefinitions.PassGoCash;
                logs.Add($"ผ่าน GO รับเงิน ${MonopolyDefinitions.PassGoCash}");
            }

            player.Position = endPosition;
            ApplyLandingEffect(room, state, player, endPosition, logs);

            if (player.Cash < 0)
            {
                HandleBankruptcy(state, player, logs);
            }

            room.TurnCounter++;
        }

        var isGameFinished = TryResolveFinish(room, out var winnerPlayerId, out var finishReason, out var roundLimitTriggered);
        if (isGameFinished)
        {
            room.Status = GameStatus.Finished;
            room.WinnerPlayerId = winnerPlayerId;
            room.FinishReason = finishReason;
            room.TurnDeadlineUtc = null;
        }
        else
        {
            AdvanceTurn(room);
            room.Status = GameStatus.Started;
            room.WinnerPlayerId = null;
            room.FinishReason = null;
            room.TurnDeadlineUtc = null;
        }

        if (logs.Count == 0)
        {
            logs.Add("จบเทิร์น");
        }

        return new TurnResult
        {
            RoomCode = room.RoomCode,
            PlayerId = player.PlayerId,
            StartPosition = startPosition,
            DiceValue = diceValue,
            BaseDiceValue = diceValue,
            EndPosition = player.Position,
            RoundLimitTriggered = roundLimitTriggered,
            IsGameFinished = isGameFinished,
            WinnerPlayerId = winnerPlayerId,
            FinishReason = finishReason,
            AutoRollReason = autoRollReason,
            ActionSummary = logs[0],
            ActionLogs = logs
        };
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

    private static void ApplyLandingEffect(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        int cell,
        List<string> logs)
    {
        var landing = state.FindCell(cell);
        if (landing is null)
        {
            logs.Add($"เดินถึงช่อง {cell}");
            return;
        }

        switch (landing.Type)
        {
            case MonopolyCellType.Go:
                logs.Add("ถึง GO");
                break;
            case MonopolyCellType.Property:
            case MonopolyCellType.Railroad:
            case MonopolyCellType.Utility:
                ResolveAssetCell(room, state, player, landing, logs);
                break;
            case MonopolyCellType.Tax:
                ResolveTaxCell(state, player, landing, logs);
                break;
            case MonopolyCellType.Chance:
                ResolveChanceCard(player, state, logs);
                break;
            case MonopolyCellType.CommunityChest:
                ResolveCommunityChest(player, state, logs);
                break;
            case MonopolyCellType.Jail:
                logs.Add("แวะเยี่ยมคุก");
                break;
            case MonopolyCellType.FreeParking:
                ResolveFreeParking(player, state, logs);
                break;
            case MonopolyCellType.GoToJail:
                player.Position = MonopolyDefinitions.JailCell;
                player.JailTurnsRemaining = MonopolyDefinitions.SkipTurnInJail;
                logs.Add($"โดนส่งเข้าคุกที่ช่อง {MonopolyDefinitions.JailCell}");
                break;
            default:
                logs.Add($"เดินถึง {landing.Name}");
                break;
        }
    }

    private static void ResolveAssetCell(
        GameRoom room,
        MonopolyRoomState _,
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

            if (player.Cash < landing.Price)
            {
                logs.Add($"เงินไม่พอซื้อ {landing.Name} (${landing.Price})");
                return;
            }

            player.Cash -= landing.Price;
            landing.OwnerPlayerId = player.PlayerId;
            logs.Add($"ซื้อ {landing.Name} ราคา ${landing.Price}");
            return;
        }

        if (landing.OwnerPlayerId == player.PlayerId)
        {
            logs.Add($"ถึง {landing.Name} (ทรัพย์สินของคุณ)");
            return;
        }

        var owner = room.FindPlayer(landing.OwnerPlayerId);
        var rent = Math.Max(0, landing.Rent);
        if (rent <= 0)
        {
            logs.Add($"ถึง {landing.Name}");
            return;
        }

        player.Cash -= rent;
        if (owner is not null && !owner.IsBankrupt)
        {
            owner.Cash += rent;
            logs.Add($"จ่ายค่าเช่า {landing.Name} ให้ {owner.DisplayName} จำนวน ${rent}");
        }
        else
        {
            logs.Add($"จ่ายค่าเช่า {landing.Name} จำนวน ${rent}");
        }
    }

    private static void ResolveTaxCell(
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
        state.FreeParkingPot += fee;
        logs.Add($"จ่ายภาษี {landing.Name} จำนวน ${fee}");
    }

    private static void ResolveFreeParking(PlayerState player, MonopolyRoomState state, List<string> logs)
    {
        if (state.FreeParkingPot <= 0)
        {
            logs.Add("Free Parking (ยังไม่มีกองกลาง)");
            return;
        }

        player.Cash += state.FreeParkingPot;
        logs.Add($"รับเงินกองกลางจาก Free Parking จำนวน ${state.FreeParkingPot}");
        state.FreeParkingPot = 0;
    }

    private static void ResolveChanceCard(PlayerState player, MonopolyRoomState state, List<string> logs)
    {
        switch (Random.Shared.Next(0, 5))
        {
            case 0:
                player.Cash += 200;
                logs.Add("Chance: ได้โบนัสลงทุน +$200");
                break;
            case 1:
                player.Cash += 120;
                logs.Add("Chance: ได้เงินปันผล +$120");
                break;
            case 2:
                player.Cash -= 100;
                state.FreeParkingPot += 100;
                logs.Add("Chance: ซ่อมแซมทรัพย์สิน -$100");
                break;
            case 3:
                player.Position = 1;
                player.Cash += MonopolyDefinitions.PassGoCash;
                logs.Add($"Chance: กลับไป GO และรับ ${MonopolyDefinitions.PassGoCash}");
                break;
            default:
                player.Position = MonopolyDefinitions.JailCell;
                player.JailTurnsRemaining = MonopolyDefinitions.SkipTurnInJail;
                logs.Add("Chance: โดนเรียกสอบภาษี ส่งเข้าคุก");
                break;
        }
    }

    private static void ResolveCommunityChest(PlayerState player, MonopolyRoomState state, List<string> logs)
    {
        switch (Random.Shared.Next(0, 5))
        {
            case 0:
                player.Cash += 100;
                logs.Add("Community Chest: รับเงินสนับสนุน +$100");
                break;
            case 1:
                player.Cash += 60;
                logs.Add("Community Chest: ได้คืนค่าบริการ +$60");
                break;
            case 2:
                player.Cash -= 60;
                state.FreeParkingPot += 60;
                logs.Add("Community Chest: จ่ายค่ากองทุน -$60");
                break;
            case 3:
                player.Cash -= 150;
                state.FreeParkingPot += 150;
                logs.Add("Community Chest: ค่าใช้จ่ายฉุกเฉิน -$150");
                break;
            default:
                player.Position = 1;
                player.Cash += MonopolyDefinitions.PassGoCash;
                logs.Add($"Community Chest: กลับ GO และรับ ${MonopolyDefinitions.PassGoCash}");
                break;
        }
    }

    private static void HandleBankruptcy(MonopolyRoomState state, PlayerState player, List<string> logs)
    {
        player.IsBankrupt = true;
        player.Cash = 0;
        player.JailTurnsRemaining = 0;

        foreach (var cell in state.Cells.Where(x => x.OwnerPlayerId == player.PlayerId))
        {
            cell.OwnerPlayerId = null;
        }

        logs.Add($"{player.DisplayName} ล้มละลาย ทรัพย์สินถูกคืนธนาคาร");
    }

    private static bool TryResolveFinish(
        GameRoom room,
        out string? winnerPlayerId,
        out string? finishReason,
        out bool roundLimitTriggered)
    {
        roundLimitTriggered = false;

        var alive = room.Players.Where(x => !x.IsBankrupt).ToArray();
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
                .OrderByDescending(x => x.Cash)
                .ThenByDescending(x => x.Position)
                .ThenBy(x => room.Players.IndexOf(x))
                .First();

            winnerPlayerId = leader.PlayerId;
            finishReason = "RoundLimitCash";
            roundLimitTriggered = true;
            return true;
        }

        winnerPlayerId = null;
        finishReason = null;
        return false;
    }

    private static void AdvanceTurn(GameRoom room)
    {
        if (room.Players.Count == 0)
        {
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
        } while (guard <= room.Players.Count &&
                 room.Players[room.CurrentTurnIndex].IsBankrupt);
    }
}
