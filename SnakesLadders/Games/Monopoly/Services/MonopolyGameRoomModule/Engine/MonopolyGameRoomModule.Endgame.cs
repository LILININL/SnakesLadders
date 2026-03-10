using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class MonopolyGameRoomModule
{
    private static void AdvanceTurn(GameRoom room, MonopolyRoomState state, List<string>? logs = null)
    {
        ClearTransientActionState(state, keepDebt: false);
        ClearTurnUpgradeState(state);
        var shouldStartVotedFinalDuel = state.FinalDuelVotePendingStart;

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
                if (room.CompletedRounds == MonopolyDefinitions.SuddenDeathStartRound)
                {
                    logs?.Add("เศรษฐกิจเดือด: ตั้งแต่รอบนี้เป็นต้นไป ค่าผ่านทางจะโตแรงขึ้นอีกขั้น");
                }
                if (!state.FinalDuelActive && ShouldAdvanceCityPriceEconomy(state))
                {
                    state.CityPriceGrowthRounds++;
                    logs?.Add($"ตลาดเมือง: ราคาเมืองทุกแปลงเพิ่มเป็น +{Math.Round(state.CityPriceGrowthRounds * MonopolyDefinitions.CityPriceGrowthPerCompletedRound * 100)}%");
                }

                if (state.FinalDuelActive)
                {
                    if (IsFinalDuelTimeoutReached(room, state))
                    {
                        logs?.Add("Final Duel: ครบ 6 รอบแล้ว เตรียมตัดสินด้วยมูลค่าสุทธิ");
                    }
                    else
                    {
                        logs?.Add(
                            $"Final Duel: รอบ {ResolveFinalDuelRound(room, state)}/{MonopolyDefinitions.FinalDuelDurationRounds} • GO {ResolveFinalDuelGoReward(room, state):N0} • ค่าผ่านทางพิเศษ +{ResolveFinalDuelRentBonusPercent(room, state)}%");
                    }
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

        if (!state.FinalDuelActive && shouldStartVotedFinalDuel && CanStartFinalDuelFromVote(room, state))
        {
            ActivateFinalDuel(
                room,
                state,
                logs,
                "Final Duel: เสียงโหวตครบแล้ว เข้าสู่ศึกปิดเกมในต้นเทิร์นนี้");
            return;
        }

        if (!state.FinalDuelActive)
        {
            ReconcileFinalDuelVotes(room, state);
        }
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
        List<string>? logs,
        out string? winnerPlayerId,
        out string? finishReason,
        out bool roundLimitTriggered)
    {
        roundLimitTriggered = false;

        var alive = room.Players.Where(player => !player.IsBankrupt).ToArray();
        if (alive.Length <= 1)
        {
            winnerPlayerId = alive.FirstOrDefault()?.PlayerId;
            finishReason = state.FinalDuelActive
                ? "FinalDuelBankruptcy"
                : "MonopolyLastStanding";
            if (state.FinalDuelActive && winnerPlayerId is not null)
            {
                var winner = room.FindPlayer(winnerPlayerId);
                logs?.Add($"Final Duel: {winner?.DisplayName ?? winnerPlayerId} ชนะทันทีเพราะอีกฝ่ายล้มละลาย");
            }
            return true;
        }

        if (state.FinalDuelActive && IsFinalDuelTimeoutReached(room, state))
        {
            var leader = alive
                .OrderByDescending(player => CalculateNetWorth(state, player.PlayerId, player.Cash, state.CityPriceGrowthRounds))
                .ThenByDescending(player => player.Cash)
                .ThenBy(player => room.Players.IndexOf(player))
                .First();

            winnerPlayerId = leader.PlayerId;
            finishReason = "FinalDuelTimeoutNetWorth";
            logs?.Add($"Final Duel: ครบ 6 รอบ ตัดสินด้วยมูลค่าสุทธิ • ผู้ชนะ {leader.DisplayName}");
            return true;
        }

        var rules = room.BoardOptions.RuleOptions;
        var maxRounds = Math.Max(1, rules.MaxRounds);
        if (!state.FinalDuelActive && rules.RoundLimitEnabled && room.CompletedRounds >= maxRounds)
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

    private static void TryActivateFinalDuel(GameRoom room, MonopolyRoomState state, List<string>? logs)
    {
        if (state.FinalDuelActive)
        {
            return;
        }

        if (state.StartedPlayerCount < MonopolyDefinitions.FinalDuelMinimumStartingPlayers)
        {
            return;
        }

        var aliveCount = room.Players.Count(player => !player.IsBankrupt);
        if (aliveCount != 2)
        {
            return;
        }

        ActivateFinalDuel(
            room,
            state,
            logs,
            "Final Duel: เหลือผู้เล่น 2 คนสุดท้ายแล้ว เข้าสู่ศึกปิดเกม");
    }

    private static void ActivateFinalDuel(
        GameRoom room,
        MonopolyRoomState state,
        List<string>? logs,
        string introLine)
    {
        state.FinalDuelActive = true;
        state.FinalDuelStartCompletedRounds = room.CompletedRounds;
        state.FinalDuelVotePendingStart = false;
        state.FinalDuelVotePlayerIds.Clear();
        logs?.Add(introLine);
        logs?.Add(
            $"Final Duel: รอบ 1/{MonopolyDefinitions.FinalDuelDurationRounds} • GO {ResolveFinalDuelGoReward(room, state):N0} • ค่าผ่านทางพิเศษ +{ResolveFinalDuelRentBonusPercent(room, state)}%");
    }

    private static void ReconcileFinalDuelVotes(GameRoom room, MonopolyRoomState state)
    {
        if (!CanOpenFinalDuelVote(room, state))
        {
            state.FinalDuelVotePendingStart = false;
            state.FinalDuelVotePlayerIds.Clear();
            return;
        }

        var alivePlayers = room.Players
            .Where(player => !player.IsBankrupt)
            .ToArray();
        var aliveIds = alivePlayers
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        state.FinalDuelVotePlayerIds.RemoveWhere(playerId => !aliveIds.Contains(playerId));

        foreach (var bot in alivePlayers.Where(player => player.IsBot))
        {
            if (ShouldBotSupportFinalDuelVote(room, state, bot, alivePlayers))
            {
                state.FinalDuelVotePlayerIds.Add(bot.PlayerId);
            }
            else
            {
                state.FinalDuelVotePlayerIds.Remove(bot.PlayerId);
            }
        }

        state.FinalDuelVotePendingStart = CountFinalDuelVotes(room, state) >= ResolveFinalDuelVoteRequired(room, state);
    }

    private static bool CanOpenFinalDuelVote(GameRoom room, MonopolyRoomState state)
    {
        if (state.FinalDuelActive ||
            state.StartedPlayerCount < MonopolyDefinitions.FinalDuelMinimumStartingPlayers)
        {
            return false;
        }

        var aliveCount = room.Players.Count(player => !player.IsBankrupt);
        if (aliveCount < 3)
        {
            return false;
        }

        var maxRounds = Math.Max(1, room.BoardOptions.RuleOptions.MaxRounds);
        var thresholdRound = Math.Max(
            1,
            (int)Math.Ceiling(maxRounds * MonopolyDefinitions.FinalDuelVoteCapProgressThreshold));
        if (room.CompletedRounds < thresholdRound)
        {
            return false;
        }

        return !state.LastBankruptcyCompletedRound.HasValue ||
               room.CompletedRounds - state.LastBankruptcyCompletedRound.Value >=
               MonopolyDefinitions.FinalDuelVoteRecentBankruptcyRounds;
    }

    private static bool CanStartFinalDuelFromVote(GameRoom room, MonopolyRoomState state) =>
        CanOpenFinalDuelVote(room, state) &&
        CountFinalDuelVotes(room, state) >= ResolveFinalDuelVoteRequired(room, state);

    private static int CountFinalDuelVotes(GameRoom room, MonopolyRoomState state)
    {
        var aliveIds = room.Players
            .Where(player => !player.IsBankrupt)
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        return state.FinalDuelVotePlayerIds.Count(aliveIds.Contains);
    }

    private static int ResolveFinalDuelVoteRequired(GameRoom room, MonopolyRoomState state)
    {
        var aliveCount = room.Players.Count(player => !player.IsBankrupt);
        return aliveCount >= 3 ? (aliveCount / 2) + 1 : 0;
    }

    private static bool ShouldBotSupportFinalDuelVote(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState bot,
        IReadOnlyList<PlayerState> alivePlayers)
    {
        if (!bot.IsBot)
        {
            return false;
        }

        var standings = alivePlayers
            .Select(player => new
            {
                Player = player,
                NetWorth = CalculateNetWorth(state, player.PlayerId, player.Cash, state.CityPriceGrowthRounds)
            })
            .OrderByDescending(entry => entry.NetWorth)
            .ThenByDescending(entry => entry.Player.Cash)
            .ThenBy(entry => room.Players.IndexOf(entry.Player))
            .ToArray();

        var myIndex = Array.FindIndex(standings, entry => entry.Player.PlayerId == bot.PlayerId);
        if (myIndex < 0)
        {
            return false;
        }

        var myRank = myIndex + 1;
        var leaderWorth = standings[0].NetWorth;
        var myWorth = standings[myIndex].NetWorth;
        var myGap = Math.Max(0, leaderWorth - myWorth);
        var effectiveCap = Math.Max(1, room.BoardOptions.RuleOptions.MaxRounds);
        var progress = Math.Clamp(room.CompletedRounds / (double)effectiveCap, 0d, 1.4d);
        var personality = ResolveFinalDuelVotePersonality(room, state, bot, myRank, myGap);

        var score = 0d;
        score += progress >= 0.90d ? 1.10d : progress >= 0.80d ? 0.70d : 0.35d;
        score += myRank > 1 ? 0.65d : -0.30d;
        score += myGap >= 550 ? 0.90d : myGap >= 280 ? 0.50d : myGap <= 120 ? -0.15d : 0d;
        score += personality switch
        {
            BotPersonality.Builder => 0.35d,
            BotPersonality.Collector => 0.18d,
            BotPersonality.Banker => -0.60d,
            _ => 0d
        };
        score += bot.BotDifficulty == BotDifficulty.Aggressive ? 0.22d : -0.04d;

        if (myRank == 1 && myGap >= 260)
        {
            score -= 0.95d;
        }

        return score >= 0.75d;
    }

    private static BotPersonality ResolveFinalDuelVotePersonality(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState bot,
        int myRank,
        int myGap)
    {
        var configured = Enum.IsDefined(typeof(BotPersonality), bot.BotPersonality)
            ? bot.BotPersonality
            : BotPersonality.Adaptive;
        if (configured != BotPersonality.Adaptive)
        {
            return configured;
        }

        var ownedCount = state.Cells.Count(cell =>
            string.Equals(cell.OwnerPlayerId, bot.PlayerId, StringComparison.Ordinal));

        if (myRank == 1 && bot.Cash >= 520)
        {
            return BotPersonality.Banker;
        }

        if (ownedCount >= 6 || myGap <= 160)
        {
            return BotPersonality.Builder;
        }

        return myGap >= 260
            ? BotPersonality.Collector
            : bot.BotDifficulty == BotDifficulty.Aggressive
                ? BotPersonality.Builder
                : BotPersonality.Collector;
    }

    private static int ResolveFinalDuelRound(GameRoom room, MonopolyRoomState state)
    {
        if (!state.FinalDuelActive)
        {
            return 0;
        }

        var elapsedRounds = Math.Max(0, room.CompletedRounds - state.FinalDuelStartCompletedRounds);
        return Math.Min(MonopolyDefinitions.FinalDuelDurationRounds, elapsedRounds + 1);
    }

    private static bool IsFinalDuelTimeoutReached(GameRoom room, MonopolyRoomState state)
    {
        if (!state.FinalDuelActive)
        {
            return false;
        }

        return room.CompletedRounds - state.FinalDuelStartCompletedRounds >= MonopolyDefinitions.FinalDuelDurationRounds;
    }

    private static int ResolveFinalDuelGoReward(GameRoom room, MonopolyRoomState state)
    {
        if (!state.FinalDuelActive)
        {
            return MonopolyDefinitions.PassGoCash;
        }

        return ResolveFinalDuelRound(room, state) <= MonopolyDefinitions.FinalDuelOpeningGoRounds
            ? MonopolyDefinitions.FinalDuelOpeningGoReward
            : 0;
    }

    private static int ResolveFinalDuelRentBonusPercent(GameRoom room, MonopolyRoomState state)
    {
        if (!state.FinalDuelActive)
        {
            return 0;
        }

        var roundIndex = Math.Clamp(
            ResolveFinalDuelRound(room, state) - 1,
            0,
            MonopolyDefinitions.FinalDuelRentBonusPercents.Length - 1);
        return MonopolyDefinitions.FinalDuelRentBonusPercents[roundIndex];
    }

    private static int ResolveEffectiveConfiguredRoundLimit(int configuredMaxRounds, int playerCount)
    {
        var configured = Math.Max(1, configuredMaxRounds);
        var normalizedPlayerCount = Math.Max(2, playerCount);
        var recommendedCap = normalizedPlayerCount switch
        {
            >= 10 => 16,
            >= 8 => 18,
            >= 6 => 22,
            >= 4 => 28,
            _ => 36
        };

        return Math.Max(10, Math.Min(configured, recommendedCap));
    }

}
