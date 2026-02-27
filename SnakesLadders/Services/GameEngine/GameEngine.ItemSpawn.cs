using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameEngine
{
    private static void PruneExpiredTransientState(GameRoom room)
    {
        room.TemporaryJumps.RemoveAll(x => x.ExpiresAtTurnCounter <= room.TurnCounter);
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
        var floor = Math.Max(3, target - Math.Max(2, room.Players.Count));
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
            if (!TrySpawnItemAheadOfPlayer(room, board, anchor, maxItemsAhead: 2))
            {
                continue;
            }
        }
    }

    private static PlayerState SelectAnchorPlayer(GameRoom room, PlayerState? priorityPlayer, int attempt)
    {
        var playersWithoutNearbyItems = room.Players
            .Where(x => !HasItemAheadOfPlayer(room, x.Position))
            .OrderBy(x => x.Position)
            .ThenBy(x => room.Players.IndexOf(x))
            .ToList();

        if (priorityPlayer is not null &&
            playersWithoutNearbyItems.Any(x => ReferenceEquals(x, priorityPlayer)) &&
            attempt % 2 == 0)
        {
            return priorityPlayer;
        }

        if (playersWithoutNearbyItems.Count > 0)
        {
            if (attempt % 3 == 0)
            {
                return playersWithoutNearbyItems[0];
            }

            var shortListCount = Math.Max(1, (int)Math.Ceiling(playersWithoutNearbyItems.Count / 2d));
            return playersWithoutNearbyItems[Random.Shared.Next(0, shortListCount)];
        }

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

        if (room.ActiveItems.Count >= ResolveMaxLiveItemCount(board.Size, room.Players.Count))
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
        => CountItemsAheadOfPlayer(room, playerPosition) > 0;

    private static int CountItemsAheadOfPlayer(GameRoom room, int playerPosition)
    {
        var minCell = playerPosition + 1;
        var maxCell = playerPosition + 6;
        return room.ActiveItems.Count(x => x.Cell >= minCell && x.Cell <= maxCell);
    }

    private static bool TrySpawnItemAheadOfPlayer(GameRoom room, BoardState board, PlayerState player, int maxItemsAhead = 1)
    {
        if (maxItemsAhead > 0 && CountItemsAheadOfPlayer(room, player.Position) >= maxItemsAhead)
        {
            return false;
        }

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
                RollItemTypeForSpawn(room)));
            return true;
        }

        return false;
    }

    private static BoardItemType RollItemTypeForSpawn(GameRoom room)
    {
        if (room.BoardOptions.GameMode == GameMode.Chaos)
        {
            // Ensure Chaos Button shows up regularly in Chaos mode.
            if (!room.ActiveItems.Any(x => x.Type == BoardItemType.ChaosButton) && Random.Shared.NextDouble() <= 0.30d)
            {
                return BoardItemType.ChaosButton;
            }

            return RollWeightedItemType(ChaosItemWeights);
        }

        return RollWeightedItemType(DefaultItemWeights);
    }

    private static BoardItemType RollWeightedItemType(IReadOnlyList<(BoardItemType Type, int Weight)> weights)
    {
        if (weights.Count == 0)
        {
            return BoardItemType.RocketBoots;
        }

        var totalWeight = weights.Sum(x => Math.Max(1, x.Weight));
        if (totalWeight <= 0)
        {
            return weights[0].Type;
        }

        var roll = Random.Shared.Next(1, totalWeight + 1);
        var cursor = 0;
        foreach (var (type, weight) in weights)
        {
            cursor += Math.Max(1, weight);
            if (roll <= cursor)
            {
                return type;
            }
        }

        return weights[^1].Type;
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
        var byBoard = (int)Math.Ceiling(boardSize / 34d);
        var byPlayers = (int)Math.Ceiling(Math.Max(2, playerCount) * 0.75d);
        return Math.Clamp(byBoard + byPlayers, 4, 14);
    }

    private static int ResolveMaxLiveItemCount(int boardSize, int playerCount)
    {
        var target = ResolveTargetItemCount(boardSize, playerCount);
        return Math.Max(target + 2, (int)Math.Ceiling(target * 1.25d));
    }
}
