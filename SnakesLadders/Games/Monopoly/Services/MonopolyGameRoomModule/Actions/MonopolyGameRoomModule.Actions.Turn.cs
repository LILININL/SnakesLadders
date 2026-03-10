using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

}
