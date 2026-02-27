using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameEngine : IGameEngine
{
    private const int OfflineAutoRollDelayMs = 700;
    private const int TurnAnimationBufferSeconds = 14;
    private const int RocketBootsBoost = 2;
    private const int LadderHackBoost = 4;
    private const int BananaSlipBack = 2;
    private const int AnchorOwnTurnDuration = 3;
    private const int TemporarySnakeDurationExtraTurns = 1;
    private const int MaxItemSpawnAttempts = 900;
    private const int ItemRefreshMinTurns = 3;
    private const int ItemRefreshMaxTurns = 5;
    private const int NearFinishItemGuardCells = 4;

    private static readonly (BoardItemType Type, int Weight)[] DefaultItemWeights =
    [
        (BoardItemType.RocketBoots, 10),
        (BoardItemType.MagnetDice, 10),
        (BoardItemType.SnakeRepellent, 9),
        (BoardItemType.LadderHack, 9),
        (BoardItemType.BananaPeel, 9),
        (BoardItemType.SwapGlove, 8),
        (BoardItemType.Anchor, 8),
        (BoardItemType.ChaosButton, 8),
        (BoardItemType.SnakeRow, 7),
        (BoardItemType.BridgeToLeader, 7),
        (BoardItemType.GlobalSnakeRound, 7)
    ];

    private static readonly (BoardItemType Type, int Weight)[] ChaosItemWeights =
    [
        (BoardItemType.RocketBoots, 8),
        (BoardItemType.MagnetDice, 8),
        (BoardItemType.SnakeRepellent, 8),
        (BoardItemType.LadderHack, 8),
        (BoardItemType.BananaPeel, 9),
        (BoardItemType.SwapGlove, 9),
        (BoardItemType.Anchor, 9),
        (BoardItemType.ChaosButton, 18),
        (BoardItemType.SnakeRow, 11),
        (BoardItemType.BridgeToLeader, 9),
        (BoardItemType.GlobalSnakeRound, 11)
    ];

    public void SeedRoomState(GameRoom room)
    {
        if (room.Board is null)
        {
            return;
        }

        PruneExpiredTransientState(room);
        RefreshBoardItemsIfNeeded(room, room.Board, forceRefresh: true);
        EnsureMinimumLiveItems(room, room.Board);
    }

    public TurnResult ResolveTurn(
        GameRoom room,
        PlayerState player,
        bool useLuckyReroll,
        ForkPathChoice? forkChoice,
        bool isAutoRoll)
    {
        if (room.Board is null)
        {
            throw new InvalidOperationException("ยังไม่ได้สร้างกระดานเกม");
        }

        var board = room.Board;
        var rules = room.BoardOptions.RuleOptions;
        var startPosition = player.Position;
        var autoRollReason = isAutoRoll
            ? (player.Connected ? "TimerExpired" : "Disconnected")
            : null;

        PruneExpiredTransientState(room);

        var frenzySnake = ResolveFrenzySnakeForTurn(room, player, board);
        room.ActiveFrenzySnake = frenzySnake;

        var comebackBoostApplied = rules.ComebackBoostEnabled && IsTrailingPlayer(room, player);
        var baseDiceValue = Random.Shared.Next(1, 7);
        var diceValue = comebackBoostApplied ? Math.Min(6, baseDiceValue + 1) : baseDiceValue;
        var comebackBoostAmount = diceValue - baseDiceValue;

        var usedLuckyReroll = false;
        if (!isAutoRoll &&
            rules.LuckyRerollEnabled &&
            useLuckyReroll &&
            player.LuckyRerollsLeft > 0)
        {
            player.LuckyRerollsLeft--;
            usedLuckyReroll = true;

            var rerolledBase = Random.Shared.Next(1, 7);
            baseDiceValue = rerolledBase;
            diceValue = comebackBoostApplied ? Math.Min(6, baseDiceValue + 1) : baseDiceValue;
            comebackBoostAmount = diceValue - baseDiceValue;
        }

        var rawTarget = startPosition + diceValue;
        var overflow = Math.Max(0, rawTarget - board.Size);

        var currentPosition = rawTarget;
        if (overflow > 0)
        {
            currentPosition = room.BoardOptions.OverflowMode switch
            {
                OverflowMode.StayPut => startPosition,
                OverflowMode.BackByOverflowX2 => Math.Max(1, startPosition - (overflow * 2)),
                _ => startPosition
            };
        }

        Jump? triggeredJump = null;
        var shieldBlockedSnake = false;
        var snakeRepellentBlockedSnake = false;
        var snakeHit = false;
        var frenzySnakeTriggered = false;
        var frenzySnakeBlockedByShield = false;
        var mercyLadderApplied = false;
        var ladderHackApplied = false;
        var ladderHackBoostAmount = 0;
        var itemEffects = new List<TurnItemEffect>();
        var blockedSnakeCells = new HashSet<int>();
        var pickedBoardItemThisTurn = false;
        var anchorAppliedThisTurn = false;

        ForkCell? triggeredForkCell = null;
        ForkPathChoice? appliedForkChoice = null;
        if (rules.ForkPathEnabled && board.ForksByCell.TryGetValue(currentPosition, out var forkCell))
        {
            appliedForkChoice = forkChoice ?? ForkPathChoice.Safe;
            triggeredForkCell = forkCell;
            currentPosition = appliedForkChoice == ForkPathChoice.Risky ? forkCell.RiskyTo : forkCell.SafeTo;
        }

        var chainGuard = 0;
        while (chainGuard++ < 32)
        {
            var interacted = false;
            var moved = false;

            var jump = FindJumpAtCell(room, board, currentPosition, frenzySnake, blockedSnakeCells);
            if (jump is not null)
            {
                interacted = true;
                var frenzyJump = IsSameJump(jump, frenzySnake);
                if (!frenzyJump && triggeredJump is null)
                {
                    triggeredJump = jump;
                }

                if (jump.Type == JumpType.Snake)
                {
                    var protection = TryConsumeSnakeProtection(player);
                    if (protection == SnakeProtectionKind.None)
                    {
                        currentPosition = jump.To;
                        snakeHit = true;
                        moved = true;
                        if (frenzyJump)
                        {
                            frenzySnakeTriggered = true;
                        }
                    }
                    else
                    {
                        blockedSnakeCells.Add(jump.From);
                        if (protection == SnakeProtectionKind.Shield)
                        {
                            shieldBlockedSnake = true;
                            if (frenzyJump)
                            {
                                frenzySnakeBlockedByShield = true;
                            }
                        }
                        else
                        {
                            snakeRepellentBlockedSnake = true;
                        }
                    }
                }
                else
                {
                    var beforeJump = currentPosition;
                    currentPosition = jump.To;
                    moved = currentPosition != beforeJump;

                    if (player.LadderHackPending)
                    {
                        player.LadderHackPending = false;
                        var beforeHack = currentPosition;
                        currentPosition = Math.Min(board.Size, currentPosition + LadderHackBoost);
                        ladderHackBoostAmount = Math.Max(0, currentPosition - beforeHack);
                        if (ladderHackBoostAmount > 0)
                        {
                            ladderHackApplied = true;
                            moved = true;
                        }
                    }
                }
            }

            var beforeTrapPosition = currentPosition;
            if (TryTriggerBananaTrap(room, board, player, ref currentPosition, out var trapCell, out var trapSummary, out var trapMoved))
            {
                interacted = true;
                moved |= trapMoved;
                itemEffects.Add(new TurnItemEffect
                {
                    ItemType = BoardItemType.BananaPeel,
                    Cell = trapCell,
                    FromPosition = beforeTrapPosition,
                    ToPosition = currentPosition,
                    Summary = trapSummary,
                    IsTrapTrigger = true
                });
            }

            if (rules.ItemsEnabled && TryTakeBoardItem(room, currentPosition, out var pickedItem))
            {
                interacted = true;
                pickedBoardItemThisTurn = true;
                if (pickedItem.Type == BoardItemType.Anchor)
                {
                    anchorAppliedThisTurn = true;
                }
                var beforeItemPosition = currentPosition;
                var summary = ApplyBoardItemEffect(pickedItem, room, board, player, ref currentPosition, out var itemMoved);
                itemEffects.Add(new TurnItemEffect
                {
                    ItemType = pickedItem.Type,
                    Cell = pickedItem.Cell,
                    FromPosition = beforeItemPosition,
                    ToPosition = currentPosition,
                    Summary = summary
                });
                moved |= itemMoved;
            }

            if (!interacted || !moved)
            {
                break;
            }
        }

        if (snakeHit)
        {
            player.ConsecutiveSnakeHits++;
        }
        else
        {
            player.ConsecutiveSnakeHits = 0;
        }

        if (rules.MercyLadderEnabled && player.MercyLadderPending)
        {
            player.MercyLadderPending = false;
            var previous = currentPosition;
            currentPosition = Math.Min(board.Size, currentPosition + Math.Max(3, rules.MercyLadderBoost));
            mercyLadderApplied = currentPosition > previous;
        }

        if (rules.MercyLadderEnabled && snakeHit && player.ConsecutiveSnakeHits >= 2)
        {
            player.MercyLadderPending = true;
        }

        currentPosition = Math.Clamp(currentPosition, 1, board.Size);

        var checkpointInterval = Math.Max(1, rules.CheckpointInterval);
        var shieldsEarned = 0;
        if (rules.CheckpointShieldEnabled)
        {
            while (player.NextCheckpoint < board.Size && currentPosition >= player.NextCheckpoint)
            {
                player.Shields++;
                shieldsEarned++;
                player.NextCheckpoint += checkpointInterval;
            }
        }

        player.Position = currentPosition;
        ConsumeAnchorTurn(player, anchorAppliedThisTurn);
        UpdatePlayerItemDryTurnStreak(player, rules.ItemsEnabled, pickedBoardItemThisTurn);
        room.TurnCounter++;

        var isGameFinished = false;
        var winnerPlayerId = default(string);
        var finishReason = default(string);
        var roundLimitTriggered = false;

        if (player.Position >= board.Size)
        {
            isGameFinished = true;
            winnerPlayerId = player.PlayerId;
            finishReason = "ReachedFinalCell";
        }
        else
        {
            AdvanceTurn(room);
            if (rules.RoundLimitEnabled && room.CompletedRounds >= rules.MaxRounds)
            {
                roundLimitTriggered = true;
                isGameFinished = true;
                var leader = room.Players
                    .OrderByDescending(x => x.Position)
                    .ThenBy(x => room.Players.IndexOf(x))
                    .First();
                winnerPlayerId = leader.PlayerId;
                finishReason = "RoundLimit";
            }
        }

        if (isGameFinished)
        {
            room.Status = GameStatus.Finished;
            room.WinnerPlayerId = winnerPlayerId;
            room.FinishReason = finishReason;
            room.TurnDeadlineUtc = null;
        }
        else
        {
            room.Status = GameStatus.Started;
            room.WinnerPlayerId = null;
            room.FinishReason = null;
            var nextTurnPlayer = room.CurrentTurnPlayer;
            if (nextTurnPlayer is null)
            {
                room.TurnDeadlineUtc = null;
            }
            else if (!nextTurnPlayer.Connected)
            {
                room.TurnDeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(OfflineAutoRollDelayMs);
            }
            else
            {
                room.TurnDeadlineUtc = rules.TurnTimerEnabled
                    ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(3, rules.TurnSeconds) + TurnAnimationBufferSeconds)
                    : null;
            }
        }

        AdvanceFrenzySnakeLifetime(room);
        PruneExpiredTransientState(room);
        if (!isGameFinished)
        {
            // Refresh/spawn happens at the end of the previous turn so players see stable board state before they roll.
            RefreshBoardItemsIfNeeded(room, board, forceRefresh: false);
            EnsureMinimumLiveItems(room, board);
            var nextPlayer = room.CurrentTurnPlayer;
            if (nextPlayer is not null)
            {
                TrySpawnPersonalItemChance(room, board, nextPlayer);
            }
        }

        return new TurnResult
        {
            RoomCode = room.RoomCode,
            PlayerId = player.PlayerId,
            StartPosition = startPosition,
            DiceValue = diceValue,
            BaseDiceValue = baseDiceValue,
            ComebackBoostAmount = comebackBoostAmount,
            EndPosition = player.Position,
            ComebackBoostApplied = comebackBoostApplied,
            UsedLuckyReroll = usedLuckyReroll,
            OverflowAmount = overflow,
            ForkChoice = appliedForkChoice,
            ForkCell = triggeredForkCell,
            TriggeredJump = triggeredJump,
            FrenzySnake = frenzySnake,
            FrenzySnakeTriggered = frenzySnakeTriggered,
            FrenzySnakeBlockedByShield = frenzySnakeBlockedByShield,
            ShieldBlockedSnake = shieldBlockedSnake,
            SnakeRepellentBlockedSnake = snakeRepellentBlockedSnake,
            MercyLadderApplied = mercyLadderApplied,
            LadderHackApplied = ladderHackApplied,
            LadderHackBoostAmount = ladderHackBoostAmount,
            ItemEffects = itemEffects,
            ShieldsEarned = shieldsEarned,
            RoundLimitTriggered = roundLimitTriggered,
            IsGameFinished = isGameFinished,
            WinnerPlayerId = winnerPlayerId,
            FinishReason = finishReason,
            AutoRollReason = autoRollReason
        };
    }

    private static bool IsTrailingPlayer(GameRoom room, PlayerState player)
    {
        if (room.Players.Count == 0)
        {
            return false;
        }

        var minimum = room.Players.Min(x => x.Position);
        var maximum = room.Players.Max(x => x.Position);
        if (maximum == minimum)
        {
            return false;
        }

        return player.Position == minimum;
    }

    private static void AdvanceTurn(GameRoom room)
    {
        if (room.Players.Count == 0)
        {
            return;
        }

        room.CurrentTurnIndex = (room.CurrentTurnIndex + 1) % room.Players.Count;
        if (room.CurrentTurnIndex == 0)
        {
            room.CompletedRounds++;
        }
    }

    private enum SnakeProtectionKind
    {
        None = 0,
        Shield = 1,
        Repellent = 2
    }
}
