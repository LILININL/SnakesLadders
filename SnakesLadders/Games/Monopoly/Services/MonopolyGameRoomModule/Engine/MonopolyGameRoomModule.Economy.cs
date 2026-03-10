using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

        return ApplyRentScaling(baseAmount, room.CompletedRounds, ResolveFinalDuelRentBonusPercent(room, state));
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
        state.LastBankruptcyCompletedRound = room.CompletedRounds;
        state.FinalDuelVotePendingStart = false;
        state.FinalDuelVotePlayerIds.Clear();

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

}
