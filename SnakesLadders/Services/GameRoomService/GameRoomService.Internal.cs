using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameRoomService
{
    private ServiceResult<RoomSnapshot> RemovePlayerUnsafe(
        string connectionId,
        string roomCode,
        string playerId,
        bool keepAsDisconnected)
    {
        _connections.Remove(connectionId);

        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
        }

        var player = room.FindPlayer(playerId);
        if (player is null)
        {
            return ServiceResult<RoomSnapshot>.Fail("ไม่พบผู้เล่น");
        }

        var leavingTurnIndex = room.Players.IndexOf(player);

        if (keepAsDisconnected)
        {
            player.Connected = false;
            player.ConnectionId = string.Empty;
        }
        else
        {
            room.Players.Remove(player);
        }

        if (room.Players.Count == 0)
        {
            _rooms.Remove(room.RoomCode);
            _chatByRoom.Remove(room.RoomCode);
            return ServiceResult<RoomSnapshot>.Ok(new RoomSnapshot
            {
                RoomCode = room.RoomCode,
                GameKey = room.GameKey,
                HostPlayerId = room.HostPlayerId,
                Status = GameStatus.Finished,
                BoardOptions = room.BoardOptions,
                Players = Array.Empty<PlayerSnapshot>(),
                CurrentTurnPlayerId = null,
                TurnCounter = room.TurnCounter,
                CompletedRounds = room.CompletedRounds,
                TurnDeadlineUtc = null,
                WinnerPlayerId = room.WinnerPlayerId,
                FinishReason = room.FinishReason,
                Board = null
            });
        }

        if (!room.Players.Any(x => x.PlayerId == room.HostPlayerId))
        {
            room.HostPlayerId = room.Players[0].PlayerId;
            room.Players[0].IsReady = true;
        }

        if (room.Status == GameStatus.Started)
        {
            if (!keepAsDisconnected)
            {
                if (leavingTurnIndex < room.CurrentTurnIndex)
                {
                    room.CurrentTurnIndex--;
                }

                if (room.CurrentTurnIndex >= room.Players.Count)
                {
                    room.CurrentTurnIndex = 0;
                }
            }

            if (room.Players.Count == 1)
            {
                room.Status = GameStatus.Finished;
                room.WinnerPlayerId = room.Players[0].PlayerId;
                room.FinishReason = "LastPlayerStanding";
                room.TurnDeadlineUtc = null;
            }
            else if (keepAsDisconnected &&
                     ReferenceEquals(room.CurrentTurnPlayer, player))
            {
                room.TurnDeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(OfflineAutoRollDelayMs);
            }
            else
            {
                room.TurnDeadlineUtc = ResolveNextTurnDeadlineUnsafe(room, room.CurrentTurnPlayer);
            }
        }

        return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
    }

    private void RemoveExistingConnectionUnsafe(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var existing))
        {
            RemovePlayerUnsafe(connectionId, existing.RoomCode, existing.PlayerId, keepAsDisconnected: false);
        }
    }

    private void BindConnectionToPlayerUnsafe(
        string connectionId,
        GameRoom room,
        PlayerState player,
        string displayName)
    {
        if (!string.IsNullOrWhiteSpace(player.ConnectionId))
        {
            _connections.Remove(player.ConnectionId);
        }

        player.ConnectionId = connectionId;
        player.Connected = true;

        var safeName = SanitizePlayerName(displayName);
        if (!string.IsNullOrWhiteSpace(safeName))
        {
            player.DisplayName = safeName;
        }

        if (player.PlayerId == room.HostPlayerId)
        {
            player.IsReady = true;
        }

        if (room.Status == GameStatus.Started &&
            ReferenceEquals(room.CurrentTurnPlayer, player))
        {
            room.TurnDeadlineUtc = ResolveNextTurnDeadlineUnsafe(room, player);
        }

        _connections[connectionId] = new ConnectionBinding(room.RoomCode, player.PlayerId);
    }

    private void UpsertLobbyPresenceUnsafe(string connectionId, string displayName)
    {
        _lobbyPresence[connectionId] = new LobbyPresenceEntry
        {
            DisplayName = SanitizePlayerName(displayName),
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeRoomCode(string roomCode) =>
        roomCode.Trim().ToUpperInvariant();

    private bool TryGetGameModule(string gameKey, out IGameRoomModule gameModule)
    {
        return _gameModules.TryGetValue(GameCatalog.Normalize(gameKey), out gameModule!);
    }

    private static string NewPlayerId() => Guid.NewGuid().ToString("N")[..8];
    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static PlayerState NewPlayerState(
        string playerId,
        string sessionId,
        string connectionId,
        string playerName,
        int avatarId,
        BoardOptions boardOptions)
    {
        var safeName = SanitizePlayerName(playerName);
        return new PlayerState
        {
            PlayerId = playerId,
            SessionId = sessionId,
            ConnectionId = connectionId,
            DisplayName = safeName,
            AvatarId = NormalizeAvatarId(avatarId),
            LuckyRerollsLeft = boardOptions.RuleOptions.LuckyRerollEnabled
                ? boardOptions.RuleOptions.LuckyRerollPerPlayer
                : 0,
            NextCheckpoint = Math.Max(1, boardOptions.RuleOptions.CheckpointInterval)
        };
    }

    private string GenerateRoomCodeUnsafe()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[6];

        do
        {
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = alphabet[Random.Shared.Next(0, alphabet.Length)];
            }
        } while (_rooms.ContainsKey(code.ToString()));

        return code.ToString();
    }

    private static string SanitizePlayerName(string input)
    {
        var name = (input ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return "ผู้เล่น";
        }

        return name.Length <= 24 ? name : name[..24];
    }

    private static int NormalizeAvatarId(int avatarId)
    {
        if (avatarId < MinAvatarId || avatarId > MaxAvatarId)
        {
            return MinAvatarId;
        }

        return avatarId;
    }

    private static RoomSnapshot ToSnapshot(GameRoom room)
    {
        var board = room.Board;
        return new RoomSnapshot
        {
            RoomCode = room.RoomCode,
            GameKey = room.GameKey,
            HostPlayerId = room.HostPlayerId,
            Status = room.Status,
            BoardOptions = room.BoardOptions,
            Players = room.Players.Select(x => new PlayerSnapshot
            {
                PlayerId = x.PlayerId,
                DisplayName = x.DisplayName,
                AvatarId = NormalizeAvatarId(x.AvatarId),
                Position = x.Position,
                Connected = x.Connected,
                IsReady = x.IsReady,
                Shields = x.Shields,
                LuckyRerollsLeft = x.LuckyRerollsLeft,
                SnakeRepellentCharges = x.SnakeRepellentCharges,
                LadderHackPending = x.LadderHackPending,
                AnchorActive = x.AnchorTurnsRemaining > 0,
                AnchorTurnsLeft = Math.Max(0, x.AnchorTurnsRemaining)
            }).ToArray(),
            CurrentTurnPlayerId = room.CurrentTurnPlayer?.PlayerId,
            TurnCounter = room.TurnCounter,
            CompletedRounds = room.CompletedRounds,
            TurnDeadlineUtc = room.TurnDeadlineUtc,
            WinnerPlayerId = room.WinnerPlayerId,
            FinishReason = room.FinishReason,
            Board = board is null
                ? null
                : new BoardSnapshot
                {
                    Size = board.Size,
                    Jumps = board.Jumps,
                    ForkCells = board.ForkCells,
                    ActiveFrenzySnake = room.ActiveFrenzySnake,
                    TemporaryJumps = room.TemporaryJumps.Select(x => x.Jump).ToArray(),
                    Items = room.ActiveItems.ToArray(),
                    BananaTrapCells = room.BananaTraps.Select(x => x.Cell).Distinct().OrderBy(x => x).ToArray()
                }
        };
    }

    private sealed record ConnectionBinding(string RoomCode, string PlayerId);

    private sealed class LobbyPresenceEntry
    {
        public required string DisplayName { get; init; }
        public required DateTimeOffset LastSeenUtc { get; init; }
    }

    private static DateTimeOffset? ResolveNextTurnDeadlineUnsafe(GameRoom room, PlayerState? nextTurnPlayer)
    {
        if (room.Status != GameStatus.Started || nextTurnPlayer is null)
        {
            return null;
        }

        if (!nextTurnPlayer.Connected)
        {
            return DateTimeOffset.UtcNow.AddMilliseconds(OfflineAutoRollDelayMs);
        }

        var rules = room.BoardOptions.RuleOptions;
        var animationBufferSeconds = room.TurnCounter > 0 ? TurnAnimationBufferSeconds : 0;
        return rules.TurnTimerEnabled
            ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(3, rules.TurnSeconds) + animationBufferSeconds)
            : null;
    }
}
