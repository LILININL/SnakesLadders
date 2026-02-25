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
        var finishRowSnakeTarget = Math.Min(count, GetFinishRowSnakeTarget(boardSize));
        AddFinishRowSnakes(
            boardSize,
            finishRowSnakeTarget,
            random,
            jumps,
            startCells,
            destinationCells);

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

    private static int GetFinishRowSnakeTarget(int boardSize)
    {
        // Ensure late game remains tense on every density:
        // small/medium boards target 2, larger boards target 3.
        return boardSize >= 250 ? 3 : 2;
    }

    private static void AddFinishRowSnakes(
        int boardSize,
        int requiredCount,
        Random random,
        List<Jump> jumps,
        HashSet<int> startCells,
        HashSet<int> destinationCells)
    {
        if (requiredCount <= 0)
        {
            return;
        }

        var finishRowStart = boardSize - ((boardSize - 1) % 10);
        var firstHead = Math.Max(4, finishRowStart);
        var lastHead = boardSize - 1;
        if (lastHead < firstHead)
        {
            return;
        }

        var heads = new List<int>(lastHead - firstHead + 1);
        for (var cell = firstHead; cell <= lastHead; cell++)
        {
            heads.Add(cell);
        }

        Shuffle(heads, random);

        var added = 0;
        foreach (var head in heads)
        {
            if (added >= requiredCount)
            {
                break;
            }

            // Tails should land below the finish row so a miss near finish is still punishing.
            var tailUpperBound = Math.Min(head - 3, finishRowStart - 1);
            if (tailUpperBound < 2)
            {
                continue;
            }

            var placed = false;
            for (var i = 0; i < 24; i++)
            {
                var tail = random.Next(2, tailUpperBound + 1);
                if (!CanPlaceJump(head, tail, startCells, destinationCells, boardSize))
                {
                    continue;
                }

                jumps.Add(new Jump(head, tail, JumpType.Snake));
                startCells.Add(head);
                destinationCells.Add(tail);
                placed = true;
                added++;
                break;
            }

            if (placed && added >= requiredCount)
            {
                break;
            }
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
        if (count <= 0 || boardSize < 4)
        {
            return;
        }

        var longBoard = boardSize >= 200;
        var maxClimbNormal = (int)Math.Ceiling(boardSize * 0.28d);
        var maxClimbEpic = (int)Math.Ceiling(boardSize * 0.35d);

        var earlyZoneUpper = (int)Math.Floor(boardSize * 0.20d);
        var earlyZoneDestinationCap = (int)Math.Floor(boardSize * 0.45d);
        var lateZoneLower = (int)Math.Ceiling(boardSize * 0.70d);
        var lateZoneDestinationCap = (int)Math.Floor(boardSize * 0.92d);

        var epicTarget = longBoard
            ? Math.Clamp((int)Math.Round(count * 0.10d, MidpointRounding.AwayFromZero), 0, count)
            : 0;
        var epicPlaced = 0;

        var attempts = 0;
        var maxAttempts = Math.Max(boardSize * 40, 1_500);

        while (jumps.Count(x => x.Type == JumpType.Ladder) < count && attempts++ < maxAttempts)
        {
            if (!longBoard)
            {
                var startClassic = random.Next(2, boardSize - 2);
                var endClassic = random.Next(startClassic + 1, boardSize);
                if (endClassic - startClassic < 3)
                {
                    continue;
                }

                if (!CanPlaceJump(startClassic, endClassic, startCells, destinationCells, boardSize))
                {
                    continue;
                }

                jumps.Add(new Jump(startClassic, endClassic, JumpType.Ladder));
                startCells.Add(startClassic);
                destinationCells.Add(endClassic);
                continue;
            }

            var currentLadderCount = jumps.Count(x => x.Type == JumpType.Ladder);
            var laddersRemaining = count - currentLadderCount;
            var epicRemaining = epicTarget - epicPlaced;
            var mustPlaceEpic = longBoard && epicRemaining > 0 && laddersRemaining <= epicRemaining;
            var pickEpic = longBoard && epicRemaining > 0 && (mustPlaceEpic || random.NextDouble() < 0.10d);

            int startMin;
            int startMaxExclusive;

            if (pickEpic)
            {
                startMin = Math.Max(2, (int)Math.Ceiling(boardSize * 0.25d));
                startMaxExclusive = Math.Min(boardSize - 1, (int)Math.Floor(boardSize * 0.70d) + 1);
            }
            else
            {
                startMin = 2;
                startMaxExclusive = boardSize - 1;
            }

            if (startMaxExclusive - startMin < 1)
            {
                continue;
            }

            var start = random.Next(startMin, startMaxExclusive);
            var maxClimb = pickEpic ? maxClimbEpic : maxClimbNormal;

            var endMin = start + 3;
            var endMax = Math.Min(boardSize - 1, start + maxClimb);

            if (longBoard && start <= earlyZoneUpper)
            {
                endMax = Math.Min(endMax, earlyZoneDestinationCap);
            }

            if (longBoard && start >= lateZoneLower)
            {
                endMax = Math.Min(endMax, lateZoneDestinationCap);
            }

            if (endMax < endMin)
            {
                continue;
            }

            var end = random.Next(endMin, endMax + 1);
            if (!CanPlaceJump(start, end, startCells, destinationCells, boardSize))
            {
                continue;
            }

            jumps.Add(new Jump(start, end, JumpType.Ladder));
            startCells.Add(start);
            destinationCells.Add(end);

            if (pickEpic)
            {
                epicPlaced++;
            }
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

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
