using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

    private static int CalculateBankLiquidationValue(MonopolyCellState cell, int completedRounds, bool isFinalDuel)
    {
        var bankBaseRatio = isFinalDuel
            ? MonopolyDefinitions.FinalDuelBankLiquidationBaseRatio
            : MonopolyDefinitions.BankLiquidationBaseRatio;
        var baseValue = cell.IsMortgaged
            ? MortgageValue(cell, completedRounds)
            : Math.Max(1, (int)Math.Floor(CalculateCityPrice(cell, completedRounds) * bankBaseRatio));
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

}
