using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed class GameRoomService(IBoardGenerator boardGenerator, IGameEngine gameEngine) : IGameRoomService
{
    private const int OfflineAutoRollDelayMs = 700;
    private const int TurnAnimationBufferSeconds = 14;
    private const int MinAvatarId = 1;
    private const int MaxAvatarId = 8;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, GameRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConnectionBinding> _connections = new();
    private readonly Dictionary<string, LobbyPresenceEntry> _lobbyPresence = new();
    private readonly Dictionary<string, List<ChatMessage>> _chatByRoom = new(StringComparer.OrdinalIgnoreCase);

    public ServiceResult<CreateRoomResponse> CreateRoom(string connectionId, CreateRoomRequest request)
    {
        lock (_sync)
        {
            RemoveExistingConnectionUnsafe(connectionId);
            UpsertLobbyPresenceUnsafe(connectionId, request.PlayerName);

            var boardOptions = request.BoardOptions ?? new BoardOptions();
            boardOptions.Normalize();
            var validation = boardOptions.Validate();
            if (validation is not null)
            {
                return ServiceResult<CreateRoomResponse>.Fail(validation);
            }

            var roomCode = GenerateRoomCodeUnsafe();
            var playerId = NewPlayerId();
            var sessionId = NewSessionId();
            var player = NewPlayerState(
                playerId,
                sessionId,
                connectionId,
                request.PlayerName,
                NormalizeAvatarId(request.AvatarId),
                boardOptions);
            player.IsReady = true;

            var room = new GameRoom
            {
                RoomCode = roomCode,
                BoardOptions = boardOptions,
                HostPlayerId = playerId
            };
            room.Players.Add(player);

            _rooms[roomCode] = room;
            _connections[connectionId] = new ConnectionBinding(roomCode, playerId);

            return ServiceResult<CreateRoomResponse>.Ok(new CreateRoomResponse
            {
                RoomCode = roomCode,
                PlayerId = playerId,
                SessionId = sessionId,
                Room = ToSnapshot(room)
            });
        }
    }

    public ServiceResult<JoinRoomResponse> JoinRoom(string connectionId, JoinRoomRequest request)
    {
        lock (_sync)
        {
            RemoveExistingConnectionUnsafe(connectionId);
            UpsertLobbyPresenceUnsafe(connectionId, request.PlayerName);

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<JoinRoomResponse>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var resumedPlayer = room.FindPlayerBySession(request.SessionId.Trim());
                if (resumedPlayer is not null)
                {
                    BindConnectionToPlayerUnsafe(connectionId, room, resumedPlayer, request.PlayerName);
                    return ServiceResult<JoinRoomResponse>.Ok(new JoinRoomResponse
                    {
                        RoomCode = roomCode,
                        PlayerId = resumedPlayer.PlayerId,
                        SessionId = resumedPlayer.SessionId,
                        Room = ToSnapshot(room)
                    });
                }
            }

            if (room.Status != GameStatus.Waiting)
            {
                return ServiceResult<JoinRoomResponse>.Fail("เกมเริ่มแล้ว ห้องนี้ปิดรับผู้เล่นเพิ่ม");
            }

            var playerId = NewPlayerId();
            var sessionId = NewSessionId();
            var player = NewPlayerState(
                playerId,
                sessionId,
                connectionId,
                request.PlayerName,
                NormalizeAvatarId(request.AvatarId),
                room.BoardOptions);
            player.IsReady = false;
            room.Players.Add(player);

            _connections[connectionId] = new ConnectionBinding(roomCode, playerId);

            return ServiceResult<JoinRoomResponse>.Ok(new JoinRoomResponse
            {
                RoomCode = roomCode,
                PlayerId = playerId,
                SessionId = sessionId,
                Room = ToSnapshot(room)
            });
        }
    }

    public ServiceResult<ResumeRoomResponse> ResumeRoom(string connectionId, ResumeRoomRequest request)
    {
        lock (_sync)
        {
            RemoveExistingConnectionUnsafe(connectionId);
            UpsertLobbyPresenceUnsafe(connectionId, request.PlayerName);

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<ResumeRoomResponse>.Fail("ไม่พบห้องที่ระบุ");
            }

            var sessionId = request.SessionId?.Trim();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return ServiceResult<ResumeRoomResponse>.Fail("จำเป็นต้องมี SessionId");
            }

            var player = room.FindPlayerBySession(sessionId);
            if (player is null)
            {
                return ServiceResult<ResumeRoomResponse>.Fail("ไม่พบ Session นี้ในห้อง");
            }

            BindConnectionToPlayerUnsafe(connectionId, room, player, request.PlayerName);

            return ServiceResult<ResumeRoomResponse>.Ok(new ResumeRoomResponse
            {
                RoomCode = roomCode,
                PlayerId = player.PlayerId,
                SessionId = player.SessionId,
                Room = ToSnapshot(room)
            });
        }
    }

    public ServiceResult<RoomSnapshot> StartGame(string connectionId, StartGameRequest request)
    {
        lock (_sync)
        {
            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (!_connections.TryGetValue(connectionId, out var binding) ||
                !binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<RoomSnapshot>.Fail("คุณไม่ได้อยู่ในห้องนี้");
            }

            if (!string.Equals(binding.PlayerId, room.HostPlayerId, StringComparison.Ordinal))
            {
                return ServiceResult<RoomSnapshot>.Fail("เฉพาะหัวห้องเท่านั้นที่เริ่มเกมได้");
            }

            if (room.Players.Count < 2)
            {
                return ServiceResult<RoomSnapshot>.Fail("ต้องมีผู้เล่นอย่างน้อย 2 คน");
            }

            var waitingPlayers = room.Players.Where(x => x.PlayerId != room.HostPlayerId).ToArray();
            if (waitingPlayers.Any(x => !x.Connected || !x.IsReady))
            {
                return ServiceResult<RoomSnapshot>.Fail("ผู้เล่นที่ไม่ใช่หัวห้องต้องออนไลน์และกดพร้อมครบ");
            }

            room.BoardOptions.Normalize();
            var validation = room.BoardOptions.Validate();
            if (validation is not null)
            {
                return ServiceResult<RoomSnapshot>.Fail(validation);
            }

            var random = room.BoardOptions.Seed.HasValue
                ? new Random(room.BoardOptions.Seed.Value)
                : Random.Shared;
            room.Board = boardGenerator.Generate(room.BoardOptions, random);

            foreach (var player in room.Players)
            {
                player.Position = 1;
                player.Shields = 0;
                player.ConsecutiveSnakeHits = 0;
                player.MercyLadderPending = false;
                player.NextCheckpoint = Math.Max(1, room.BoardOptions.RuleOptions.CheckpointInterval);
                player.LuckyRerollsLeft = room.BoardOptions.RuleOptions.LuckyRerollEnabled
                    ? room.BoardOptions.RuleOptions.LuckyRerollPerPlayer
                    : 0;
            }

            room.Status = GameStatus.Started;
            room.CurrentTurnIndex = random.Next(0, room.Players.Count);
            room.TurnCounter = 0;
            room.CompletedRounds = 0;
            room.WinnerPlayerId = null;
            room.FinishReason = null;
            room.ActiveFrenzySnake = null;
            room.ActiveFrenzySnakeTurnsLeft = 0;
            room.FrenzyNoSpawnStreak = 0;
            room.TurnDeadlineUtc = ResolveNextTurnDeadlineUnsafe(room, room.CurrentTurnPlayer);

            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public ServiceResult<TurnEnvelope> RollDice(string connectionId, RollDiceRequest request, bool isAutoRoll = false)
    {
        lock (_sync)
        {
            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<TurnEnvelope>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (room.Status != GameStatus.Started)
            {
                return ServiceResult<TurnEnvelope>.Fail("เกมยังไม่อยู่ในสถานะกำลังเล่น");
            }

            var currentPlayer = room.CurrentTurnPlayer;
            if (currentPlayer is null)
            {
                return ServiceResult<TurnEnvelope>.Fail("ไม่พบผู้เล่นที่กำลังมีตา");
            }

            PlayerState actor;
            if (isAutoRoll)
            {
                actor = currentPlayer;
            }
            else
            {
                if (!_connections.TryGetValue(connectionId, out var binding) ||
                    !binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult<TurnEnvelope>.Fail("คุณไม่ได้อยู่ในห้องนี้");
                }

                var player = room.FindPlayer(binding.PlayerId);
                if (player is null)
                {
                    return ServiceResult<TurnEnvelope>.Fail("ไม่พบผู้เล่น");
                }

                actor = player;
            }

            if (!ReferenceEquals(actor, currentPlayer))
            {
                return ServiceResult<TurnEnvelope>.Fail("ยังไม่ถึงตาคุณ");
            }

            var result = gameEngine.ResolveTurn(
                room,
                actor,
                request.UseLuckyReroll,
                request.ForkChoice,
                isAutoRoll);

            return ServiceResult<TurnEnvelope>.Ok(new TurnEnvelope
            {
                Turn = result,
                Room = ToSnapshot(room)
            });
        }
    }

    public ServiceResult<RoomSnapshot> SetReady(string connectionId, SetReadyRequest request)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("คุณยังไม่ได้เข้าห้อง");
            }

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<RoomSnapshot>.Fail("คุณไม่ได้อยู่ในห้องนี้");
            }

            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (room.Status != GameStatus.Waiting)
            {
                return ServiceResult<RoomSnapshot>.Fail("เปลี่ยนสถานะพร้อมได้เฉพาะก่อนเริ่มเกม");
            }

            var player = room.FindPlayer(binding.PlayerId);
            if (player is null)
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบผู้เล่น");
            }

            if (player.PlayerId == room.HostPlayerId)
            {
                player.IsReady = true;
                return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
            }

            player.IsReady = request.IsReady;
            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public ServiceResult<RoomSnapshot> SetAvatar(string connectionId, SetAvatarRequest request)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("คุณยังไม่ได้เข้าห้อง");
            }

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<RoomSnapshot>.Fail("คุณไม่ได้อยู่ในห้องนี้");
            }

            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (room.Status != GameStatus.Waiting)
            {
                return ServiceResult<RoomSnapshot>.Fail("เปลี่ยน Avatar ได้เฉพาะก่อนเริ่มเกม");
            }

            var player = room.FindPlayer(binding.PlayerId);
            if (player is null)
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบผู้เล่น");
            }

            if (player.IsReady && player.PlayerId != room.HostPlayerId)
            {
                return ServiceResult<RoomSnapshot>.Fail("ยกเลิกพร้อมก่อน แล้วค่อยเปลี่ยน Avatar");
            }

            player.AvatarId = NormalizeAvatarId(request.AvatarId);
            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

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

                var result = gameEngine.ResolveTurn(
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
                LuckyRerollsLeft = x.LuckyRerollsLeft
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
                    ActiveFrenzySnake = room.ActiveFrenzySnake
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
