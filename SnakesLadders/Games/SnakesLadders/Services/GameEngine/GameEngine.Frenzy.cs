using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameEngine
{
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
}
