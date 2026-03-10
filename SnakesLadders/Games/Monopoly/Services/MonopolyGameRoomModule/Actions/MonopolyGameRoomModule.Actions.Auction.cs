using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

}
