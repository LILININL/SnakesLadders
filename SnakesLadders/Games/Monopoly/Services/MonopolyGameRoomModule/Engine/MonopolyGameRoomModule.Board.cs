using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
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

        AwardPassGoCash(room, state, player, CountPassGoEvents(start, Math.Max(0, steps), boardSize), logs, "ผ่าน GO");

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
                ChargeBankDebt(state, player, 180, "โอกาส: โดนค่าปรับจราจร", logs);
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
                ChargeBankDebt(state, player, 180, "การ์ดชุมชน: จ่ายค่าหมอ", logs);
                return;
            case 5:
                ChargeBankDebt(state, player, 260, "การ์ดชุมชน: จ่ายค่าเล่าเรียน", logs);
                return;
            case 6:
                player.Cash += 150;
                logs.Add("การ์ดชุมชน: ได้เงินสนับสนุนจากรัฐ +฿150");
                return;
            case 7:
                ChargeOwnedPropertyFee(state, player, 70, logs, "การ์ดชุมชน: จ่ายค่าบำรุงเมือง");
                return;
            case 8:
                CreditByOwnedAssets(state, player, 30, logs, "การ์ดชุมชน: รายได้ค่าเช่าสะสม");
                return;
            case 9:
                ChargeByMortgagedAssets(state, player, 130, logs, "การ์ดชุมชน: เมืองของคุณโดนตรวจภาษี");
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
        AwardPassGoCash(room, state, player, target < current ? 1 : 0, logs, "ผ่าน GO");

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

}
