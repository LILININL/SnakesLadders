using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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
            return "หนี้จากผู้เล่นต้องแก้ด้วยการจำนองหรือขายสิ่งปลูกสร้างก่อน ถ้ายังปิดหนี้ไม่ได้ระบบจะตัดสินล้มละลาย";
        }
        else
        {
            var saleValue = CalculateBankLiquidationValue(cell, state.CityPriceGrowthRounds, state.FinalDuelActive);
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
}
