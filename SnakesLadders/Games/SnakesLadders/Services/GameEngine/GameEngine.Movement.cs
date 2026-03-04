using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameEngine
{
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

    private static bool IsAnchorActive(PlayerState player, GameRoom _) =>
        player.AnchorTurnsRemaining > 0;

    private static void ConsumeAnchorTurn(PlayerState player, bool anchorAppliedThisTurn)
    {
        if (anchorAppliedThisTurn || player.AnchorTurnsRemaining <= 0)
        {
            return;
        }

        player.AnchorTurnsRemaining = Math.Max(0, player.AnchorTurnsRemaining - 1);
    }

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
}
