using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameEngine
{
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
            BoardItemType.Anchor => ApplyAnchor(player),
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
        var created = PlaceBananaTrapsForLeadingPlayers(room, board, player, currentPosition, out var trapCells);
        if (created <= 0)
        {
            return "Banana Peel: หาช่องวางกับดักไม่สำเร็จ";
        }

        var preview = string.Join(", ", trapCells.Take(4));
        var suffix = trapCells.Count > 4 ? ", ..." : string.Empty;
        return $"Banana Peel: วางกับดักถาวร {created} จุดที่ช่อง {preview}{suffix}";
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

    private static string ApplyAnchor(PlayerState player)
    {
        player.AnchorTurnsRemaining = Math.Max(player.AnchorTurnsRemaining, AnchorOwnTurnDuration);
        return $"Anchor: กันการสลับ/ผลักถอยได้อีก {player.AnchorTurnsRemaining} เทิร์นของคุณ";
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
        var expiresAt = room.TurnCounter + Math.Max(2, room.Players.Count + TemporarySnakeDurationExtraTurns);
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
            ? $"Snake Row: แถว {rowStart}-{rowEnd} มีงู {created} ตัว (ช่องรอด {safeCell}) อยู่ครบหนึ่งรอบผู้เล่น"
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
        var expiresAt = room.TurnCounter + Math.Max(2, room.Players.Count + TemporarySnakeDurationExtraTurns);
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

    private static int PlaceBananaTrapsForLeadingPlayers(
        GameRoom room,
        BoardState board,
        PlayerState ownerPlayer,
        int currentPosition,
        out List<int> trapCells)
    {
        trapCells = new List<int>();

        // Picking Banana Peel again replaces previous traps from the same owner.
        room.BananaTraps.RemoveAll(x => string.Equals(x.OwnerPlayerId, ownerPlayer.PlayerId, StringComparison.Ordinal));

        var targets = room.Players
            .Where(x => x.Position > currentPosition)
            .OrderByDescending(x => x.Position)
            .ThenBy(x => room.Players.IndexOf(x))
            .ToArray();

        if (targets.Length == 0)
        {
            var fallbackTargets = room.Players
                .Where(x => !ReferenceEquals(x, ownerPlayer))
                .OrderByDescending(x => x.Position)
                .ThenBy(x => room.Players.IndexOf(x))
                .ToArray();
            targets = fallbackTargets;
        }

        foreach (var target in targets)
        {
            if (TryPlaceSingleBananaTrap(room, board, ownerPlayer.PlayerId, target.Position + 1, target.Position + 4, out var trapCell))
            {
                trapCells.Add(trapCell);
            }
        }

        if (trapCells.Count == 0)
        {
            // Keep one fallback trap in front of caster if no valid leader slots found.
            if (TryPlaceSingleBananaTrap(room, board, ownerPlayer.PlayerId, currentPosition + 2, currentPosition + 8, out var trapCell))
            {
                trapCells.Add(trapCell);
            }
        }

        return trapCells.Count;
    }

    private static bool TryPlaceSingleBananaTrap(
        GameRoom room,
        BoardState board,
        string ownerPlayerId,
        int minCell,
        int maxCell,
        out int trapCell)
    {
        trapCell = 0;
        var clampedMin = Math.Max(3, minCell);
        var clampedMax = Math.Min(board.Size - 1, maxCell);
        if (clampedMax < clampedMin)
        {
            return false;
        }

        var occupied = room.Players.Select(x => x.Position).ToHashSet();
        var candidates = Enumerable.Range(clampedMin, clampedMax - clampedMin + 1)
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        foreach (var cell in candidates)
        {
            if (!IsAllowedBananaTrapCell(room, board, cell, occupied))
            {
                continue;
            }

            trapCell = cell;
            room.BananaTraps.Add(new BananaTrapState
            {
                TrapId = Guid.NewGuid().ToString("N")[..10],
                Cell = cell,
                OwnerPlayerId = ownerPlayerId,
                ExpiresAtTurnCounter = int.MaxValue
            });
            return true;
        }

        return false;
    }

    private static bool IsAllowedBananaTrapCell(
        GameRoom room,
        BoardState board,
        int cell,
        IReadOnlySet<int> occupiedCells)
    {
        if (occupiedCells.Contains(cell))
        {
            return false;
        }

        if (board.JumpsByFrom.ContainsKey(cell) || board.ForksByCell.ContainsKey(cell))
        {
            return false;
        }

        if (room.ActiveItems.Any(x => x.Cell == cell))
        {
            return false;
        }

        if (room.BananaTraps.Any(x => x.Cell == cell))
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
            .FirstOrDefault(x => x.Cell == actorPosition);
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
}
