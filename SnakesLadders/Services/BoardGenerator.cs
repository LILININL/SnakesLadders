using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class BoardGenerator : IBoardGenerator
{
    public BoardState Generate(BoardOptions options, Random random)
    {
        var (baseSnakes, baseLadders) = options.DensityMode switch
        {
            DensityMode.Low => (4, 5),
            DensityMode.Medium => (6, 7),
            DensityMode.High => (8, 9),
            _ => (6, 7)
        };

        var factor = (int)Math.Ceiling(options.BoardSize / 100d);
        var snakeCount = baseSnakes * factor;
        var ladderCount = baseLadders * factor;

        if (options.RuleOptions.MarathonSpeedupEnabled &&
            options.BoardSize >= options.RuleOptions.MarathonThreshold)
        {
            ladderCount = (int)Math.Ceiling(ladderCount * options.RuleOptions.MarathonLadderMultiplier);
        }

        var jumps = new List<Jump>(snakeCount + ladderCount);
        var startCells = new HashSet<int> { 1, options.BoardSize };
        var destinationCells = new HashSet<int> { 1, options.BoardSize };

        AddSnakes(options.BoardSize, snakeCount, random, jumps, startCells, destinationCells);
        AddLadders(options.BoardSize, ladderCount, random, jumps, startCells, destinationCells);

        var forkCells = options.RuleOptions.ForkPathEnabled
            ? GenerateForkCells(options.BoardSize, random, startCells, destinationCells, jumps)
            : new List<ForkCell>();

        return new BoardState
        {
            Size = options.BoardSize,
            Jumps = jumps,
            ForkCells = forkCells,
            JumpsByFrom = jumps.ToDictionary(x => x.From),
            ForksByCell = forkCells.ToDictionary(x => x.Cell)
        };
    }

    private static void AddSnakes(
        int boardSize,
        int count,
        Random random,
        List<Jump> jumps,
        HashSet<int> startCells,
        HashSet<int> destinationCells)
    {
        var attempts = 0;
        var maxAttempts = Math.Max(boardSize * 40, 1_500);

        while (jumps.Count(x => x.Type == JumpType.Snake) < count && attempts++ < maxAttempts)
        {
            var head = random.Next(4, boardSize);
            var tail = random.Next(2, head);

            if (head - tail < 3)
            {
                continue;
            }

            if (!CanPlaceJump(head, tail, startCells, destinationCells, boardSize))
            {
                continue;
            }

            jumps.Add(new Jump(head, tail, JumpType.Snake));
            startCells.Add(head);
            destinationCells.Add(tail);
        }
    }

    private static void AddLadders(
        int boardSize,
        int count,
        Random random,
        List<Jump> jumps,
        HashSet<int> startCells,
        HashSet<int> destinationCells)
    {
        var attempts = 0;
        var maxAttempts = Math.Max(boardSize * 40, 1_500);

        while (jumps.Count(x => x.Type == JumpType.Ladder) < count && attempts++ < maxAttempts)
        {
            var start = random.Next(2, boardSize - 2);
            var end = random.Next(start + 1, boardSize);

            if (end - start < 3)
            {
                continue;
            }

            if (!CanPlaceJump(start, end, startCells, destinationCells, boardSize))
            {
                continue;
            }

            jumps.Add(new Jump(start, end, JumpType.Ladder));
            startCells.Add(start);
            destinationCells.Add(end);
        }
    }

    private static bool CanPlaceJump(
        int from,
        int to,
        HashSet<int> startCells,
        HashSet<int> destinationCells,
        int boardSize)
    {
        if (from <= 1 || from >= boardSize || to <= 1 || to >= boardSize || from == to)
        {
            return false;
        }

        if (startCells.Contains(from) || startCells.Contains(to))
        {
            return false;
        }

        // Avoid chains and ambiguous cell behaviors by keeping endpoints unique.
        if (destinationCells.Contains(from) || destinationCells.Contains(to))
        {
            return false;
        }

        return true;
    }

    private static List<ForkCell> GenerateForkCells(
        int boardSize,
        Random random,
        HashSet<int> startCells,
        HashSet<int> destinationCells,
        IReadOnlyList<Jump> jumps)
    {
        var snakeHeads = jumps
            .Where(x => x.Type == JumpType.Snake)
            .Select(x => x.From)
            .ToHashSet();

        var forkCount = Math.Clamp((int)Math.Ceiling(boardSize / 120d), 1, 64);
        var forks = new List<ForkCell>(forkCount);
        var usedForkCells = new HashSet<int>();

        var attempts = 0;
        var maxAttempts = Math.Max(boardSize * 20, 1_000);

        while (forks.Count < forkCount && attempts++ < maxAttempts)
        {
            var cell = random.Next(2, boardSize - 1);

            if (startCells.Contains(cell) || destinationCells.Contains(cell) || usedForkCells.Contains(cell))
            {
                continue;
            }

            var safeTo = Math.Min(boardSize - 1, cell + random.Next(4, 12));
            var riskyTo = Math.Min(boardSize - 1, cell + random.Next(10, 25));

            if (safeTo <= cell || riskyTo <= cell || safeTo == riskyTo)
            {
                continue;
            }

            var safeAttempts = 0;
            while (snakeHeads.Contains(safeTo) && safeAttempts++ < 8)
            {
                safeTo++;
            }

            if (safeTo >= boardSize)
            {
                continue;
            }

            forks.Add(new ForkCell(cell, safeTo, riskyTo));
            usedForkCells.Add(cell);
        }

        return forks;
    }
}
