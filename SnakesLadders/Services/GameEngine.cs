using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class GameEngine : IGameEngine
{
    private const int OfflineAutoRollDelayMs = 700;
    private const int TurnAnimationBufferSeconds = 14;

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

        var frenzySnake = ShouldSpawnFrenzySnake(room) ? GenerateFrenzySnake(board) : null;
        room.ActiveFrenzySnake = frenzySnake;

        var comebackBoostApplied = rules.ComebackBoostEnabled && IsTrailingPlayer(room, player);
        var baseDice = Random.Shared.Next(1, 7);
        var diceValue = comebackBoostApplied ? Math.Min(6, baseDice + 1) : baseDice;

        var usedLuckyReroll = false;
        if (!isAutoRoll &&
            rules.LuckyRerollEnabled &&
            useLuckyReroll &&
            player.LuckyRerollsLeft > 0)
        {
            player.LuckyRerollsLeft--;
            usedLuckyReroll = true;

            var rerolledBase = Random.Shared.Next(1, 7);
            diceValue = comebackBoostApplied ? Math.Min(6, rerolledBase + 1) : rerolledBase;
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

        ForkCell? triggeredForkCell = null;
        ForkPathChoice? appliedForkChoice = null;
        if (rules.ForkPathEnabled && board.ForksByCell.TryGetValue(currentPosition, out var forkCell))
        {
            appliedForkChoice = forkChoice ?? ForkPathChoice.Safe;
            triggeredForkCell = forkCell;
            currentPosition = appliedForkChoice == ForkPathChoice.Risky ? forkCell.RiskyTo : forkCell.SafeTo;
        }

        Jump? triggeredJump = null;
        var shieldBlockedSnake = false;
        var snakeHit = false;

        if (board.JumpsByFrom.TryGetValue(currentPosition, out var boardJump))
        {
            triggeredJump = boardJump;
            if (boardJump.Type == JumpType.Snake)
            {
                if (TryConsumeShield(player))
                {
                    shieldBlockedSnake = true;
                }
                else
                {
                    currentPosition = boardJump.To;
                    snakeHit = true;
                }
            }
            else
            {
                currentPosition = boardJump.To;
            }
        }

        if (frenzySnake is not null && currentPosition == frenzySnake.From)
        {
            if (TryConsumeShield(player))
            {
                shieldBlockedSnake = true;
            }
            else
            {
                currentPosition = frenzySnake.To;
                snakeHit = true;
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

        var mercyLadderApplied = false;
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

        room.ActiveFrenzySnake = null;

        return new TurnResult
        {
            RoomCode = room.RoomCode,
            PlayerId = player.PlayerId,
            StartPosition = startPosition,
            DiceValue = diceValue,
            EndPosition = player.Position,
            ComebackBoostApplied = comebackBoostApplied,
            UsedLuckyReroll = usedLuckyReroll,
            OverflowAmount = overflow,
            ForkChoice = appliedForkChoice,
            ForkCell = triggeredForkCell,
            TriggeredJump = triggeredJump,
            FrenzySnake = frenzySnake,
            ShieldBlockedSnake = shieldBlockedSnake,
            MercyLadderApplied = mercyLadderApplied,
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

    private static bool ShouldSpawnFrenzySnake(GameRoom room)
    {
        var rules = room.BoardOptions.RuleOptions;
        if (!rules.SnakeFrenzyEnabled || rules.SnakeFrenzyIntervalTurns <= 0)
        {
            return false;
        }

        return (room.TurnCounter + 1) % rules.SnakeFrenzyIntervalTurns == 0;
    }

    private static Jump? GenerateFrenzySnake(BoardState board)
    {
        var attempts = 0;
        while (attempts++ < 200)
        {
            var head = Random.Shared.Next(5, board.Size);
            var tail = Random.Shared.Next(2, head);
            if (head - tail < 4)
            {
                continue;
            }

            if (board.JumpsByFrom.ContainsKey(head) || board.JumpsByFrom.ContainsKey(tail))
            {
                continue;
            }

            if (board.ForksByCell.ContainsKey(head) || board.ForksByCell.ContainsKey(tail))
            {
                continue;
            }

            return new Jump(head, tail, JumpType.Snake, true);
        }

        return null;
    }

    private static bool TryConsumeShield(PlayerState player)
    {
        if (player.Shields <= 0)
        {
            return false;
        }

        player.Shields--;
        return true;
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
}
