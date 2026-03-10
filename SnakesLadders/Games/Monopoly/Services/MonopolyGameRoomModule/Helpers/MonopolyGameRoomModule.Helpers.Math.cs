using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

    private static int ApplyRentScaling(int amount, int completedRounds, int finalDuelBonusPercent = 0)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var accelerated = amount * MonopolyDefinitions.RentAccelerationMultiplier;
        var normalizedRounds = Math.Max(0, completedRounds);
        var growthMultiplier = 1d + (normalizedRounds * MonopolyDefinitions.RentGrowthPerCompletedRound);
        if (normalizedRounds > MonopolyDefinitions.SuddenDeathStartRound)
        {
            growthMultiplier +=
                (normalizedRounds - MonopolyDefinitions.SuddenDeathStartRound) *
                MonopolyDefinitions.SuddenDeathExtraRentGrowthPerCompletedRound;
        }
        var duelMultiplier = 1d + (Math.Max(0, finalDuelBonusPercent) / 100d);
        return Math.Max(1, (int)Math.Ceiling(accelerated * growthMultiplier * duelMultiplier));
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

    private static void AwardPassGoCash(GameRoom room, MonopolyRoomState state, PlayerState player, int passCount, List<string> logs, string reason)
    {
        if (passCount <= 0)
        {
            return;
        }

        var total = ResolveFinalDuelGoReward(room, state) * passCount;
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
