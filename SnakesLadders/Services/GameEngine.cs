using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class GameEngine : IGameEngine
{
    private const int OfflineAutoRollDelayMs = 700;
    private const int TurnAnimationBufferSeconds = 14;
    private const int RocketBootsBoost = 2;
    private const int LadderHackBoost = 4;
    private const int BananaSlipBack = 2;
    private const int MaxItemSpawnAttempts = 900;
    private const int ItemRefreshMinTurns = 3;
    private const int ItemRefreshMaxTurns = 5;
    private const int NearFinishItemGuardCells = 4;

    private static readonly BoardItemType[] ItemPool = Enum.GetValues<BoardItemType>();

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
        RefreshBoardItemsIfNeeded(room, board, forceRefresh: false);
        EnsureMinimumLiveItems(room, board);
        TrySpawnPersonalItemChance(room, board, player);

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

        if (rules.ItemsEnabled && overflow <= 0 && diceValue > 0)
        {
            currentPosition = ResolveForwardMovementWithPassThroughItems(
                room,
                board,
                player,
                startPosition,
                diceValue,
                itemEffects,
                ref pickedBoardItemThisTurn);
        }

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
                    Summary = trapSummary
                });
            }

            if (rules.ItemsEnabled && TryTakeBoardItem(room, currentPosition, out var pickedItem))
            {
                interacted = true;
                pickedBoardItemThisTurn = true;
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
        RefreshBoardItemsIfNeeded(room, board, forceRefresh: false);
        EnsureMinimumLiveItems(room, board);

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

    private static Jump? ResolveFrenzySnakeForTurn(GameRoom room, PlayerState currentPlayer, BoardState board)
    {
        if (room.ActiveFrenzySnake is not null && room.ActiveFrenzySnakeTurnsLeft > 0)
        {
            return room.ActiveFrenzySnake;
        }

        room.ActiveFrenzySnake = null;
        room.ActiveFrenzySnakeTurnsLeft = 0;
        return TrySpawnFrenzySnake(room, currentPlayer, board);
    }

    private static Jump? TrySpawnFrenzySnake(GameRoom room, PlayerState currentPlayer, BoardState board)
    {
        var rules = room.BoardOptions.RuleOptions;
        if (!rules.SnakeFrenzyEnabled)
        {
            room.ActiveFrenzySnakeTurnsLeft = 0;
            room.FrenzyNoSpawnStreak = 0;
            return null;
        }

        var playerCount = Math.Max(2, room.Players.Count);
        var checkInterval = ResolveFrenzyCheckInterval(playerCount);
        if (checkInterval <= 0 || (room.TurnCounter + 1) % checkInterval != 0)
        {
            return null;
        }

        var spawnChance = ResolveFrenzySpawnChance(playerCount, room.FrenzyNoSpawnStreak);
        if (Random.Shared.NextDouble() > spawnChance)
        {
            room.FrenzyNoSpawnStreak++;
            return null;
        }

        var snake = GenerateFrenzySnakeAheadOfPlayer(room, board, currentPlayer.Position, room.Players);
        if (snake is null)
        {
            room.FrenzyNoSpawnStreak++;
            return null;
        }

        room.ActiveFrenzySnake = snake;
        room.ActiveFrenzySnakeTurnsLeft = Math.Max(1, room.Players.Count);
        room.FrenzyNoSpawnStreak = 0;
        return snake;
    }

    private static void AdvanceFrenzySnakeLifetime(GameRoom room)
    {
        if (room.ActiveFrenzySnake is null)
        {
            room.ActiveFrenzySnakeTurnsLeft = 0;
            return;
        }

        if (room.ActiveFrenzySnakeTurnsLeft <= 0)
        {
            room.ActiveFrenzySnake = null;
            room.ActiveFrenzySnakeTurnsLeft = 0;
            return;
        }

        room.ActiveFrenzySnakeTurnsLeft--;
        if (room.ActiveFrenzySnakeTurnsLeft <= 0)
        {
            room.ActiveFrenzySnake = null;
            room.ActiveFrenzySnakeTurnsLeft = 0;
        }
    }

    private static int ResolveFrenzyCheckInterval(int playerCount)
    {
        var normalizedPlayerCount = Math.Max(2, playerCount);
        return Math.Clamp(6 - normalizedPlayerCount, 2, 4);
    }

    private static double ResolveFrenzySpawnChance(int playerCount, int noSpawnStreak)
    {
        var normalizedPlayerCount = Math.Max(2, playerCount);
        var normalizedStreak = Math.Max(0, noSpawnStreak);
        var chance = 0.35d + ((normalizedPlayerCount - 2) * 0.07d) + (normalizedStreak * 0.08d);
        return Math.Clamp(chance, 0.35d, 0.80d);
    }

    private static Jump? GenerateFrenzySnakeAheadOfPlayer(
        GameRoom room,
        BoardState board,
        int playerPosition,
        IReadOnlyList<PlayerState> players)
    {
        var occupiedCells = players
            .Select(x => x.Position)
            .ToHashSet();

        var minHead = Math.Max(5, playerPosition + 2);
        var maxHead = Math.Min(board.Size - 3, playerPosition + 8);
        if (maxHead < minHead)
        {
            return null;
        }

        var headCandidates = Enumerable.Range(minHead, maxHead - minHead + 1)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        foreach (var head in headCandidates)
        {
            if (!CanUseCellForTemporaryJump(head, room, board, occupiedCells))
            {
                continue;
            }

            var minTail = Math.Max(2, head - 9);
            var maxTail = head - 3;
            if (maxTail < minTail)
            {
                continue;
            }

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var tail = Random.Shared.Next(minTail, maxTail + 1);
                if (!CanUseCellForTemporaryJump(tail, room, board, occupiedCells))
                {
                    continue;
                }

                if (head - tail < 3)
                {
                    continue;
                }

                return new Jump(head, tail, JumpType.Snake, true);
            }
        }

        return GenerateFrenzySnakeFallback(room, board, occupiedCells);
    }

    private static Jump? GenerateFrenzySnakeFallback(GameRoom room, BoardState board, IReadOnlySet<int> occupiedCells)
    {
        var attempts = 0;
        while (attempts++ < 220)
        {
            var head = Random.Shared.Next(5, Math.Max(6, board.Size - 1));
            var tail = Random.Shared.Next(2, head);
            if (head - tail < 4)
            {
                continue;
            }

            if (!CanUseCellForTemporaryJump(head, room, board, occupiedCells))
            {
                continue;
            }

            if (!CanUseCellForTemporaryJump(tail, room, board, occupiedCells))
            {
                continue;
            }

            return new Jump(head, tail, JumpType.Snake, true);
        }

        return null;
    }

    private static int ResolveForwardMovementWithPassThroughItems(
        GameRoom room,
        BoardState board,
        PlayerState player,
        int startPosition,
        int diceSteps,
        List<TurnItemEffect> itemEffects,
        ref bool pickedBoardItemThisTurn)
    {
        var currentPosition = startPosition;
        var steps = Math.Max(0, diceSteps);
        for (var i = 0; i < steps; i++)
        {
            if (currentPosition >= board.Size)
            {
                currentPosition = board.Size;
                break;
            }

            currentPosition++;
            if (!TryTakeBoardItem(room, currentPosition, out var pickedItem))
            {
                continue;
            }

            pickedBoardItemThisTurn = true;
            var beforeItemPosition = currentPosition;
            var summary = ApplyBoardItemEffect(pickedItem, room, board, player, ref currentPosition, out _);
            itemEffects.Add(new TurnItemEffect
            {
                ItemType = pickedItem.Type,
                Cell = pickedItem.Cell,
                FromPosition = beforeItemPosition,
                ToPosition = currentPosition,
                Summary = summary
            });
        }

        return Math.Clamp(currentPosition, 1, board.Size);
    }

    private static bool TryTakeBoardItem(GameRoom room, int cell, out BoardItem item)
    {
        for (var i = 0; i < room.ActiveItems.Count; i++)
        {
            if (room.ActiveItems[i].Cell != cell)
            {
                continue;
            }

            item = room.ActiveItems[i];
            room.ActiveItems.RemoveAt(i);
            return true;
        }

        item = null!;
        return false;
    }

    private static string ApplyBoardItemEffect(
        BoardItem item,
        GameRoom room,
        BoardState board,
        PlayerState player,
        ref int currentPosition,
        out bool positionChanged)
    {
        positionChanged = false;
        return item.Type switch
        {
            BoardItemType.RocketBoots => ApplyRocketBoots(board, ref currentPosition, out positionChanged),
            BoardItemType.MagnetDice => ApplyMagnetDice(board, ref currentPosition, out positionChanged),
            BoardItemType.SnakeRepellent => ApplySnakeRepellent(player),
            BoardItemType.LadderHack => ApplyLadderHack(player),
            BoardItemType.BananaPeel => ApplyBananaPeel(room, board, player, currentPosition),
            BoardItemType.SwapGlove => ApplySwapGlove(room, board, player, ref currentPosition, out positionChanged),
            BoardItemType.Anchor => ApplyAnchor(room, player),
            BoardItemType.ChaosButton => ApplyChaosButton(room, board, player, ref currentPosition, out positionChanged),
            BoardItemType.SnakeRow => ApplySnakeRow(room, board),
            BoardItemType.BridgeToLeader => ApplyBridgeToLeader(room, board, player, ref currentPosition, out positionChanged),
            BoardItemType.GlobalSnakeRound => ApplyGlobalSnakeRound(room, board),
            _ => "ไอเท็มนี้ยังไม่มีเอฟเฟกต์"
        };
    }

    private static string ApplyRocketBoots(BoardState board, ref int currentPosition, out bool moved)
    {
        var before = currentPosition;
        currentPosition = Math.Min(board.Size, currentPosition + RocketBootsBoost);
        moved = currentPosition > before;
        return moved
            ? $"Rocket Boots: พุ่ง +{RocketBootsBoost} ไปช่อง {currentPosition}"
            : "Rocket Boots: แตะแล้วแต่ตันเส้นชัย";
    }

    private static string ApplyMagnetDice(BoardState board, ref int currentPosition, out bool moved)
    {
        var delta = Random.Shared.Next(0, 2) == 0 ? -1 : 1;
        var before = currentPosition;
        currentPosition = Math.Clamp(currentPosition + delta, 1, board.Size);
        moved = currentPosition != before;
        if (!moved)
        {
            return "Magnet Dice: แรงดูดไม่เกิดผล";
        }

        return delta > 0
            ? $"Magnet Dice: ดันเพิ่ม +1 ไปช่อง {currentPosition}"
            : $"Magnet Dice: ดึงถอย -1 ไปช่อง {currentPosition}";
    }

    private static string ApplySnakeRepellent(PlayerState player)
    {
        player.SnakeRepellentCharges = Math.Min(2, player.SnakeRepellentCharges + 1);
        return $"Snake Repellent: กันงูสะสม {player.SnakeRepellentCharges} ชั้น";
    }

    private static string ApplyLadderHack(PlayerState player)
    {
        player.LadderHackPending = true;
        return "Ladder Hack: บันไดครั้งถัดไปจะพุ่งเพิ่ม";
    }

    private static string ApplyBananaPeel(GameRoom room, BoardState board, PlayerState player, int currentPosition)
    {
        return TryPlaceBananaTrap(room, board, player.PlayerId, currentPosition, out var trapCell)
            ? $"Banana Peel: วางกับดักที่ช่อง {trapCell}"
            : "Banana Peel: หาช่องวางกับดักไม่สำเร็จ";
    }

    private static string ApplySwapGlove(
        GameRoom room,
        BoardState board,
        PlayerState currentPlayer,
        ref int currentPosition,
        out bool moved)
    {
        moved = false;
        var actorPosition = currentPosition;

        var target = room.Players
            .Where(x => !ReferenceEquals(x, currentPlayer) && x.Position > actorPosition)
            .OrderBy(x => x.Position)
            .ThenBy(x => room.Players.IndexOf(x))
            .FirstOrDefault();

        if (target is null)
        {
            return "Swap Glove: ไม่มีคนที่อยู่เหนือกว่าให้สลับ";
        }

        if (IsAnchorActive(target, room))
        {
            return $"Swap Glove: {target.DisplayName} เปิด Anchor กันไว้";
        }

        var targetPosition = target.Position;
        target.Position = currentPosition;
        currentPosition = Math.Clamp(targetPosition, 1, board.Size);
        moved = true;

        return $"Swap Glove: สลับตำแหน่งกับ {target.DisplayName}";
    }

    private static string ApplyAnchor(GameRoom room, PlayerState player)
    {
        player.AnchorProtectedUntilTurnCounter = Math.Max(
            player.AnchorProtectedUntilTurnCounter,
            room.TurnCounter + Math.Max(1, room.Players.Count));
        return "Anchor: กันการสลับ/ผลักถอยจนถึงตาถัดไปของคุณ";
    }

    private static string ApplyChaosButton(
        GameRoom room,
        BoardState board,
        PlayerState currentPlayer,
        ref int currentPosition,
        out bool moved)
    {
        moved = false;
        var choice = Random.Shared.Next(0, 4);
        switch (choice)
        {
            case 0:
                {
                    var movedPlayers = 0;
                    foreach (var player in room.Players)
                    {
                        if (ReferenceEquals(player, currentPlayer))
                        {
                            var before = currentPosition;
                            currentPosition = Math.Min(board.Size, currentPosition + 1);
                            moved |= currentPosition != before;
                            if (currentPosition != before)
                            {
                                movedPlayers++;
                            }
                            continue;
                        }

                        var beforeOther = player.Position;
                        player.Position = Math.Min(Math.Max(1, board.Size - 1), player.Position + 1);
                        if (player.Position != beforeOther)
                        {
                            movedPlayers++;
                        }
                    }

                    return $"Chaos Button: ลมส่งท้าย ดันผู้เล่นขยับ +1 ({movedPlayers} คน)";
                }
            case 1:
                {
                    var affected = 0;
                    foreach (var player in room.Players)
                    {
                        if (ReferenceEquals(player, currentPlayer))
                        {
                            var before = currentPosition;
                            currentPosition = Math.Max(1, currentPosition - 1);
                            moved |= currentPosition != before;
                            if (currentPosition != before)
                            {
                                affected++;
                            }
                            continue;
                        }

                        if (IsAnchorActive(player, room))
                        {
                            continue;
                        }

                        var beforeOther = player.Position;
                        player.Position = Math.Max(1, player.Position - 2);
                        if (player.Position != beforeOther)
                        {
                            affected++;
                        }
                    }

                    return $"Chaos Button: แผ่นดินไหว ผลักผู้เล่นถอย ({affected} คน)";
                }
            case 2:
                {
                    foreach (var player in room.Players)
                    {
                        player.Shields++;
                    }

                    return "Chaos Button: ฝนโล่ตก ทุกคนได้โล่ +1";
                }
            default:
                {
                    var actorPosition = currentPosition;
                    var entries = room.Players
                        .Select((player, index) => new
                        {
                            Player = player,
                            Index = index,
                            Position = ReferenceEquals(player, currentPlayer) ? actorPosition : player.Position
                        })
                        .OrderByDescending(x => x.Position)
                        .ThenBy(x => x.Index)
                        .ToArray();

                    if (entries.Length < 2 || entries[0].Position == entries[^1].Position)
                    {
                        return "Chaos Button: ไพ่สลับตำแหน่งไม่ทำงาน (คะแนนสูสีกัน)";
                    }

                    var leader = entries[0];
                    var trailer = entries[^1];
                    if (IsAnchorActive(leader.Player, room) || IsAnchorActive(trailer.Player, room))
                    {
                        return "Chaos Button: ไพ่สลับโดน Anchor กันไว้";
                    }

                    SetEffectivePosition(currentPlayer, leader.Player, trailer.Position, ref currentPosition);
                    SetEffectivePosition(currentPlayer, trailer.Player, leader.Position, ref currentPosition);
                    moved = true;
                    return $"Chaos Button: สลับหัวแถว {leader.Player.DisplayName} กับท้ายแถว {trailer.Player.DisplayName}";
                }
        }
    }

    private static string ApplySnakeRow(GameRoom room, BoardState board)
    {
        if (room.Players.Count == 0)
        {
            return "Snake Row: ไม่มีผู้เล่นในห้อง";
        }

        var trailingPosition = room.Players.Min(x => x.Position);
        var beneficiary = room.Players
            .Where(x => x.Position == trailingPosition)
            .OrderBy(x => room.Players.IndexOf(x))
            .First();

        var seedCell = Math.Min(board.Size - 1, Math.Max(3, beneficiary.Position + 7));
        var rowStart = ((seedCell - 1) / 10) * 10 + 1;
        var rowEnd = Math.Min(board.Size - 1, rowStart + 9);

        if (rowEnd < 5)
        {
            return "Snake Row: แถวเป้าหมายไม่พร้อมสร้างงู";
        }

        var occupied = room.Players.Select(x => x.Position).ToHashSet();
        var candidates = Enumerable.Range(rowStart, rowEnd - rowStart + 1)
            .Where(x => x >= 5 && !occupied.Contains(x))
            .ToArray();

        if (candidates.Length < 3)
        {
            return "Snake Row: ช่องในแถวไม่พอสำหรับสร้างงู";
        }

        var safeCell = candidates[Random.Shared.Next(candidates.Length)];
        var expiresAt = room.TurnCounter + Math.Max(2, room.Players.Count);
        var created = 0;

        foreach (var head in candidates)
        {
            if (head == safeCell)
            {
                continue;
            }

            var tail = Math.Max(2, head - Random.Shared.Next(3, 9));
            if (!CanPlaceTemporaryJump(room, board, head, tail, occupied))
            {
                continue;
            }

            room.TemporaryJumps.Add(new TemporaryJumpState
            {
                EffectId = Guid.NewGuid().ToString("N")[..10],
                Jump = new Jump(head, tail, JumpType.Snake, true),
                ExpiresAtTurnCounter = expiresAt,
                Source = "SnakeRow"
            });
            created++;
        }

        return created > 0
            ? $"Snake Row: แถว {rowStart}-{rowEnd} มีงู {created} ตัว (ช่องรอด {safeCell})"
            : "Snake Row: สร้างงูไม่สำเร็จ";
    }

    private static string ApplyBridgeToLeader(
        GameRoom room,
        BoardState board,
        PlayerState player,
        ref int currentPosition,
        out bool moved)
    {
        moved = false;
        var leader = room.Players
            .Where(x => !ReferenceEquals(x, player))
            .OrderByDescending(x => x.Position)
            .ThenBy(x => room.Players.IndexOf(x))
            .FirstOrDefault();

        if (leader is null || leader.Position <= currentPosition)
        {
            return "Bridge to Leader: ไม่มีคนที่นำหน้าให้ไล่ทัน";
        }

        var before = currentPosition;
        currentPosition = Math.Clamp(leader.Position, 1, board.Size);
        moved = currentPosition != before;
        return moved
            ? $"Bridge to Leader: พุ่งไปช่อง {currentPosition} เท่าผู้นำ"
            : "Bridge to Leader: ตำแหน่งเท่าผู้นำอยู่แล้ว";
    }

    private static string ApplyGlobalSnakeRound(GameRoom room, BoardState board)
    {
        var targetCount = Math.Clamp(room.Players.Count * 3, 6, 18);
        var occupied = room.Players.Select(x => x.Position).ToHashSet();
        var expiresAt = room.TurnCounter + Math.Max(2, room.Players.Count);
        var created = 0;
        var attempts = 0;

        while (created < targetCount && attempts++ < 900)
        {
            var head = Random.Shared.Next(5, Math.Max(6, board.Size - 1));
            var tailMin = Math.Max(2, head - 12);
            var tailMax = head - 3;
            if (tailMax < tailMin)
            {
                continue;
            }

            var tail = Random.Shared.Next(tailMin, tailMax + 1);
            if (!CanPlaceTemporaryJump(room, board, head, tail, occupied))
            {
                continue;
            }

            room.TemporaryJumps.Add(new TemporaryJumpState
            {
                EffectId = Guid.NewGuid().ToString("N")[..10],
                Jump = new Jump(head, tail, JumpType.Snake, true),
                ExpiresAtTurnCounter = expiresAt,
                Source = "GlobalSnakeRound"
            });
            created++;
        }

        return created > 0
            ? $"Global Snake Round: เพิ่มงูชั่วคราว {created} ตัว จนครบรอบผู้เล่น"
            : "Global Snake Round: พื้นที่กระดานแน่นเกินไป งูไม่เกิดเพิ่ม";
    }

    private static bool TryPlaceBananaTrap(
        GameRoom room,
        BoardState board,
        string ownerPlayerId,
        int currentPosition,
        out int trapCell)
    {
        trapCell = 0;
        var occupied = room.Players.Select(x => x.Position).ToHashSet();
        var minCell = Math.Max(3, currentPosition + 2);
        var maxCell = Math.Min(board.Size - 1, currentPosition + 10);
        if (maxCell < minCell)
        {
            return false;
        }

        var candidates = Enumerable.Range(minCell, maxCell - minCell + 1)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        foreach (var cell in candidates)
        {
            if (occupied.Contains(cell))
            {
                continue;
            }

            if (board.JumpsByFrom.ContainsKey(cell) || board.ForksByCell.ContainsKey(cell))
            {
                continue;
            }

            if (room.ActiveItems.Any(x => x.Cell == cell))
            {
                continue;
            }

            if (room.BananaTraps.Any(x => x.Cell == cell))
            {
                continue;
            }

            if (room.TemporaryJumps.Any(x => x.Jump.From == cell || x.Jump.To == cell))
            {
                continue;
            }

            if (room.ActiveFrenzySnake is not null &&
                (room.ActiveFrenzySnake.From == cell || room.ActiveFrenzySnake.To == cell))
            {
                continue;
            }

            trapCell = cell;
            room.BananaTraps.Add(new BananaTrapState
            {
                TrapId = Guid.NewGuid().ToString("N")[..10],
                Cell = cell,
                OwnerPlayerId = ownerPlayerId,
                ExpiresAtTurnCounter = room.TurnCounter + Math.Max(2, room.Players.Count)
            });
            return true;
        }

        return false;
    }

    private static bool TryTriggerBananaTrap(
        GameRoom room,
        BoardState board,
        PlayerState player,
        ref int currentPosition,
        out int trapCell,
        out string summary,
        out bool moved)
    {
        moved = false;
        trapCell = currentPosition;
        summary = string.Empty;
        var actorPosition = currentPosition;

        var trap = room.BananaTraps
            .FirstOrDefault(x => x.Cell == actorPosition && !string.Equals(x.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal));
        if (trap is null)
        {
            return false;
        }

        room.BananaTraps.Remove(trap);
        trapCell = trap.Cell;

        if (IsAnchorActive(player, room))
        {
            summary = "Banana Peel: ติดกับดักแต่ Anchor กันแรงถอยไว้ได้";
            return true;
        }

        var before = currentPosition;
        currentPosition = Math.Clamp(currentPosition - BananaSlipBack, 1, board.Size);
        moved = currentPosition != before;
        summary = moved
            ? $"Banana Peel: ลื่นถอย {before} -> {currentPosition}"
            : "Banana Peel: ลื่นแต่ไม่ถอย";
        return true;
    }

    private static Jump? FindJumpAtCell(
        GameRoom room,
        BoardState board,
        int cell,
        Jump? frenzySnake,
        IReadOnlySet<int> blockedSnakeCells)
    {
        if (board.JumpsByFrom.TryGetValue(cell, out var boardJump))
        {
            if (boardJump.Type == JumpType.Snake && blockedSnakeCells.Contains(cell))
            {
                return null;
            }

            return boardJump;
        }

        var temporaryJump = room.TemporaryJumps
            .Select(x => x.Jump)
            .FirstOrDefault(x => x.From == cell);
        if (temporaryJump is not null)
        {
            if (temporaryJump.Type == JumpType.Snake && blockedSnakeCells.Contains(cell))
            {
                return null;
            }

            return temporaryJump;
        }

        if (frenzySnake is not null && frenzySnake.From == cell && !blockedSnakeCells.Contains(cell))
        {
            return frenzySnake;
        }

        return null;
    }

    private static bool IsSameJump(Jump? left, Jump? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return left.From == right.From &&
               left.To == right.To &&
               left.Type == right.Type;
    }

    private static SnakeProtectionKind TryConsumeSnakeProtection(PlayerState player)
    {
        if (player.SnakeRepellentCharges > 0)
        {
            player.SnakeRepellentCharges--;
            return SnakeProtectionKind.Repellent;
        }

        if (player.Shields > 0)
        {
            player.Shields--;
            return SnakeProtectionKind.Shield;
        }

        return SnakeProtectionKind.None;
    }

    private static bool IsAnchorActive(PlayerState player, GameRoom room) =>
        player.AnchorProtectedUntilTurnCounter > room.TurnCounter;

    private static void SetEffectivePosition(
        PlayerState currentPlayer,
        PlayerState targetPlayer,
        int newPosition,
        ref int currentPosition)
    {
        if (ReferenceEquals(currentPlayer, targetPlayer))
        {
            currentPosition = newPosition;
            return;
        }

        targetPlayer.Position = newPosition;
    }

    private static bool CanPlaceTemporaryJump(
        GameRoom room,
        BoardState board,
        int head,
        int tail,
        IReadOnlySet<int> occupiedCells)
    {
        if (head <= 2 || tail <= 1 || head >= board.Size || tail >= board.Size)
        {
            return false;
        }

        if (head - tail < 3)
        {
            return false;
        }

        if (!CanUseCellForTemporaryJump(head, room, board, occupiedCells))
        {
            return false;
        }

        if (!CanUseCellForTemporaryJump(tail, room, board, occupiedCells))
        {
            return false;
        }

        return true;
    }

    private static bool CanUseCellForTemporaryJump(
        int cell,
        GameRoom room,
        BoardState board,
        IReadOnlySet<int> occupiedCells)
    {
        if (cell <= 2 || cell >= board.Size)
        {
            return false;
        }

        if (occupiedCells.Contains(cell))
        {
            return false;
        }

        if (board.JumpsByFrom.ContainsKey(cell) || board.ForksByCell.ContainsKey(cell))
        {
            return false;
        }

        if (room.ActiveItems.Any(x => x.Cell == cell) ||
            room.BananaTraps.Any(x => x.Cell == cell))
        {
            return false;
        }

        if (room.TemporaryJumps.Any(x => x.Jump.From == cell || x.Jump.To == cell))
        {
            return false;
        }

        if (room.ActiveFrenzySnake is not null &&
            (room.ActiveFrenzySnake.From == cell || room.ActiveFrenzySnake.To == cell))
        {
            return false;
        }

        return true;
    }

    private static void PruneExpiredTransientState(GameRoom room)
    {
        room.TemporaryJumps.RemoveAll(x => x.ExpiresAtTurnCounter <= room.TurnCounter);
        room.BananaTraps.RemoveAll(x => x.ExpiresAtTurnCounter <= room.TurnCounter);
    }

    private static void UpdatePlayerItemDryTurnStreak(PlayerState player, bool itemsEnabled, bool pickedBoardItemThisTurn)
    {
        if (!itemsEnabled)
        {
            player.ItemDryTurnStreak = 0;
            return;
        }

        if (pickedBoardItemThisTurn)
        {
            player.ItemDryTurnStreak = 0;
            return;
        }

        player.ItemDryTurnStreak = Math.Min(12, player.ItemDryTurnStreak + 1);
    }

    private static void RefreshBoardItemsIfNeeded(GameRoom room, BoardState board, bool forceRefresh)
    {
        if (!room.BoardOptions.RuleOptions.ItemsEnabled)
        {
            room.ActiveItems.Clear();
            room.BananaTraps.Clear();
            room.TemporaryJumps.RemoveAll(x => string.Equals(x.Source, "SnakeRow", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(x.Source, "GlobalSnakeRound", StringComparison.OrdinalIgnoreCase));
            room.NextItemRefreshAtTurnCounter = 0;
            return;
        }

        if (room.Players.Count == 0)
        {
            room.ActiveItems.Clear();
            room.NextItemRefreshAtTurnCounter = 0;
            return;
        }

        if (room.NextItemRefreshAtTurnCounter <= 0)
        {
            ScheduleNextItemRefresh(room);
        }

        if (!forceRefresh && room.TurnCounter < room.NextItemRefreshAtTurnCounter)
        {
            return;
        }

        RemoveItemsBehindAllPlayers(room);
        ReplenishItems(room, board, ResolveTargetItemCount(board.Size, room.Players.Count), priorityPlayer: null);
        ScheduleNextItemRefresh(room);
    }

    private static void EnsureMinimumLiveItems(GameRoom room, BoardState board)
    {
        if (!room.BoardOptions.RuleOptions.ItemsEnabled)
        {
            return;
        }

        var target = ResolveTargetItemCount(board.Size, room.Players.Count);
        var floor = Math.Max(4, target - Math.Max(2, room.Players.Count));
        if (room.ActiveItems.Count >= floor)
        {
            return;
        }

        var trailingPlayer = room.Players
            .OrderBy(x => x.Position)
            .ThenBy(x => room.Players.IndexOf(x))
            .FirstOrDefault();
        ReplenishItems(room, board, floor, trailingPlayer);
    }

    private static void RemoveItemsBehindAllPlayers(GameRoom room)
    {
        if (room.Players.Count == 0 || room.ActiveItems.Count == 0)
        {
            return;
        }

        var minimumPosition = room.Players.Min(x => x.Position);
        room.ActiveItems.RemoveAll(x => x.Cell < minimumPosition);
    }

    private static void ReplenishItems(GameRoom room, BoardState board, int targetCount, PlayerState? priorityPlayer)
    {
        if (targetCount <= 0 || room.Players.Count == 0)
        {
            return;
        }

        var attempts = 0;
        while (room.ActiveItems.Count < targetCount && attempts++ < MaxItemSpawnAttempts)
        {
            var anchor = SelectAnchorPlayer(room, priorityPlayer, attempts);
            if (!TrySpawnItemAheadOfPlayer(room, board, anchor))
            {
                continue;
            }
        }
    }

    private static PlayerState SelectAnchorPlayer(GameRoom room, PlayerState? priorityPlayer, int attempt)
    {
        if (priorityPlayer is not null && attempt % 2 == 0)
        {
            return priorityPlayer;
        }

        if (attempt % 3 == 0)
        {
            return room.Players
                .OrderBy(x => x.Position)
                .ThenBy(x => room.Players.IndexOf(x))
                .First();
        }

        return room.Players[Random.Shared.Next(0, room.Players.Count)];
    }

    private static void TrySpawnPersonalItemChance(GameRoom room, BoardState board, PlayerState player)
    {
        if (!room.BoardOptions.RuleOptions.ItemsEnabled)
        {
            return;
        }

        var attemptTurn = Math.Max(1, player.ItemDryTurnStreak + 1);
        var spawnChance = ResolvePersonalSpawnChance(attemptTurn);
        if (spawnChance <= 0d)
        {
            return;
        }

        if (HasItemAheadOfPlayer(room, player.Position))
        {
            return;
        }

        if (Random.Shared.NextDouble() > spawnChance)
        {
            return;
        }

        TrySpawnItemAheadOfPlayer(room, board, player);
    }

    private static double ResolvePersonalSpawnChance(int attemptTurn)
    {
        if (attemptTurn <= 2) return 0d;
        if (attemptTurn == 3) return 0.50d;
        if (attemptTurn == 4) return 0.65d;
        if (attemptTurn == 5) return 0.85d;
        return 1.0d;
    }

    private static bool HasItemAheadOfPlayer(GameRoom room, int playerPosition)
    {
        var minCell = playerPosition + 1;
        var maxCell = playerPosition + 6;
        return room.ActiveItems.Any(x => x.Cell >= minCell && x.Cell <= maxCell);
    }

    private static bool TrySpawnItemAheadOfPlayer(GameRoom room, BoardState board, PlayerState player)
    {
        var minCell = player.Position + 1;
        var maxCell = Math.Min(board.Size - 1, player.Position + 6);
        if (maxCell < minCell)
        {
            return false;
        }

        var candidates = Enumerable.Range(minCell, maxCell - minCell + 1)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        foreach (var cell in candidates)
        {
            if (!IsAllowedItemCell(room, board, cell))
            {
                continue;
            }

            room.ActiveItems.Add(new BoardItem(
                Guid.NewGuid().ToString("N")[..10],
                cell,
                ItemPool[Random.Shared.Next(0, ItemPool.Length)]));
            return true;
        }

        return false;
    }

    private static bool IsAllowedItemCell(GameRoom room, BoardState board, int cell)
    {
        if (cell <= 2 || cell >= board.Size)
        {
            return false;
        }

        var nearFinishThreshold = Math.Max(3, board.Size - NearFinishItemGuardCells);
        if (cell >= nearFinishThreshold)
        {
            return false;
        }

        if (room.Players.Any(x => x.Position == cell))
        {
            return false;
        }

        if (board.JumpsByFrom.ContainsKey(cell) || board.ForksByCell.ContainsKey(cell))
        {
            return false;
        }

        if (room.ActiveItems.Any(x => x.Cell == cell) ||
            room.BananaTraps.Any(x => x.Cell == cell))
        {
            return false;
        }

        if (room.TemporaryJumps.Any(x => x.Jump.From == cell || x.Jump.To == cell))
        {
            return false;
        }

        if (room.ActiveFrenzySnake is not null &&
            (room.ActiveFrenzySnake.From == cell || room.ActiveFrenzySnake.To == cell))
        {
            return false;
        }

        return true;
    }

    private static void ScheduleNextItemRefresh(GameRoom room)
    {
        var offset = Random.Shared.Next(ItemRefreshMinTurns, ItemRefreshMaxTurns + 1);
        room.NextItemRefreshAtTurnCounter = room.TurnCounter + offset;
    }

    private static int ResolveTargetItemCount(int boardSize, int playerCount)
    {
        var byBoard = (int)Math.Ceiling(boardSize / 25d);
        var byPlayers = Math.Max(2, playerCount);
        return Math.Clamp(byBoard + byPlayers + 1, 8, 28);
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
