using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameRoomService
{
    public ServiceResult<RoomSnapshot> ResetFinishedGame(string roomCode)
    {
        lock (_sync)
        {
            var normalized = NormalizeRoomCode(roomCode);
            if (!_rooms.TryGetValue(normalized, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (room.Status != GameStatus.Finished)
            {
                return ServiceResult<RoomSnapshot>.Fail("ห้องนี้ยังไม่จบเกม");
            }

            room.Players.RemoveAll(x => !x.Connected);
            if (room.Players.Count == 0)
            {
                _rooms.Remove(normalized);
                _chatByRoom.Remove(normalized);
                return ServiceResult<RoomSnapshot>.Fail("ห้องถูกปิดเพราะไม่มีผู้เล่นออนไลน์อยู่แล้ว");
            }

            if (!room.Players.Any(x => x.PlayerId == room.HostPlayerId))
            {
                room.HostPlayerId = room.Players[0].PlayerId;
            }

            var checkpoint = Math.Max(1, room.BoardOptions.RuleOptions.CheckpointInterval);
            foreach (var player in room.Players)
            {
                player.Position = 1;
                player.Shields = 0;
                player.ConsecutiveSnakeHits = 0;
                player.MercyLadderPending = false;
                player.SnakeRepellentCharges = 0;
                player.LadderHackPending = false;
                player.AnchorTurnsRemaining = 0;
                player.ItemDryTurnStreak = 0;
                player.NextCheckpoint = checkpoint;
                player.LuckyRerollsLeft = room.BoardOptions.RuleOptions.LuckyRerollEnabled
                    ? room.BoardOptions.RuleOptions.LuckyRerollPerPlayer
                    : 0;
                player.IsReady = player.PlayerId == room.HostPlayerId;
            }

            room.Status = GameStatus.Waiting;
            room.Board = null;
            room.CurrentTurnIndex = 0;
            room.TurnCounter = 0;
            room.CompletedRounds = 0;
            room.WinnerPlayerId = null;
            room.FinishReason = null;
            room.ActiveFrenzySnake = null;
            room.ActiveFrenzySnakeTurnsLeft = 0;
            room.FrenzyNoSpawnStreak = 0;
            room.NextItemRefreshAtTurnCounter = 0;
            room.ActiveItems.Clear();
            room.TemporaryJumps.Clear();
            room.BananaTraps.Clear();
            room.TurnDeadlineUtc = null;

            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public ServiceResult<ChatMessage> SendChat(string connectionId, SendChatRequest request)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<ChatMessage>.Fail("คุณยังไม่ได้เข้าห้อง");
            }

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<ChatMessage>.Fail("คุณไม่ได้อยู่ในห้องนี้");
            }

            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<ChatMessage>.Fail("ไม่พบห้องที่ระบุ");
            }

            var player = room.FindPlayer(binding.PlayerId);
            if (player is null)
            {
                return ServiceResult<ChatMessage>.Fail("ไม่พบผู้เล่น");
            }

            var message = (request.Message ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                return ServiceResult<ChatMessage>.Fail("ข้อความว่างเปล่า");
            }

            if (message.Length > 300)
            {
                return ServiceResult<ChatMessage>.Fail("ข้อความยาวเกินไป");
            }

            var chat = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString("N")[..12],
                RoomCode = roomCode,
                PlayerId = player.PlayerId,
                DisplayName = player.DisplayName,
                Message = message,
                SentAtUtc = DateTimeOffset.UtcNow
            };

            if (!_chatByRoom.TryGetValue(roomCode, out var roomChat))
            {
                roomChat = new List<ChatMessage>();
                _chatByRoom[roomCode] = roomChat;
            }

            roomChat.Add(chat);
            if (roomChat.Count > 100)
            {
                roomChat.RemoveRange(0, roomChat.Count - 100);
            }

            return ServiceResult<ChatMessage>.Ok(chat);
        }
    }

    public ServiceResult<RoomSnapshot> LeaveRoom(string connectionId, string? roomCode = null)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อยังไม่ได้ผูกกับห้อง");
            }

            if (roomCode is not null &&
                !binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อนี้ไม่ได้อยู่ในห้องที่ระบุ");
            }

            var keepSeat = _rooms.TryGetValue(binding.RoomCode, out var room) &&
                           room.Status == GameStatus.Started;
            return RemovePlayerUnsafe(connectionId, binding.RoomCode, binding.PlayerId, keepAsDisconnected: keepSeat);
        }
    }

    public ServiceResult<RoomSnapshot> HandleDisconnect(string connectionId)
    {
        lock (_sync)
        {
            _lobbyPresence.Remove(connectionId);

            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อยังไม่ได้ผูกกับห้อง");
            }

            var keepSeat = _rooms.TryGetValue(binding.RoomCode, out var room) &&
                           room.Status == GameStatus.Started;
            return RemovePlayerUnsafe(connectionId, binding.RoomCode, binding.PlayerId, keepAsDisconnected: keepSeat);
        }
    }

    public ServiceResult<RoomSnapshot> GetRoom(string roomCode)
    {
        lock (_sync)
        {
            var normalized = NormalizeRoomCode(roomCode);
            if (!_rooms.TryGetValue(normalized, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public IReadOnlyList<PublicRoomSummary> GetPublicRooms()
    {
        lock (_sync)
        {
            return _rooms.Values
                .Where(x => x.Status == GameStatus.Waiting)
                .OrderByDescending(x => x.Players.Count)
                .ThenBy(x => x.RoomCode, StringComparer.Ordinal)
                .Take(100)
                .Select(x =>
                {
                    var host = x.FindPlayer(x.HostPlayerId);
                    return new PublicRoomSummary
                    {
                        RoomCode = x.RoomCode,
                        Status = x.Status,
                        HostName = host?.DisplayName ?? "หัวห้อง",
                        PlayerCount = x.Players.Count,
                        BoardSize = x.BoardOptions.BoardSize,
                        DensityMode = x.BoardOptions.DensityMode
                    };
                })
                .ToArray();
        }
    }

    public void UpsertLobbyPresence(string connectionId, string displayName)
    {
        lock (_sync)
        {
            UpsertLobbyPresenceUnsafe(connectionId, displayName);
        }
    }

    public IReadOnlyList<LobbyOnlineUser> GetLobbyOnlineUsers()
    {
        lock (_sync)
        {
            return _lobbyPresence
                .Select(x =>
                {
                    _connections.TryGetValue(x.Key, out var binding);
                    return new LobbyOnlineUser
                    {
                        ConnectionId = x.Key,
                        DisplayName = x.Value.DisplayName,
                        RoomCode = binding?.RoomCode
                    };
                })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ConnectionId, StringComparer.Ordinal)
                .Take(200)
                .ToArray();
        }
    }

    public IReadOnlyList<AutoRollDispatch> ProcessExpiredTurns()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var dispatches = new List<AutoRollDispatch>();

            foreach (var room in _rooms.Values.Where(x =>
                         x.Status == GameStatus.Started &&
                         x.TurnDeadlineUtc is not null &&
                         x.TurnDeadlineUtc <= now))
            {
                var currentPlayer = room.CurrentTurnPlayer;
                if (currentPlayer is null)
                {
                    continue;
                }

                var result = _gameEngine.ResolveTurn(
                    room,
                    currentPlayer,
                    useLuckyReroll: false,
                    forkChoice: ForkPathChoice.Safe,
                    isAutoRoll: true);

                dispatches.Add(new AutoRollDispatch
                {
                    RoomCode = room.RoomCode,
                    PlayerId = currentPlayer.PlayerId,
                    Payload = new TurnEnvelope
                    {
                        Turn = result,
                        Room = ToSnapshot(room)
                    }
                });
            }

            return dispatches;
        }
    }

}
