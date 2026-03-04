using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameRoomService : IGameRoomService
{
    private const int OfflineAutoRollDelayMs = 700;
    private const int TurnAnimationBufferSeconds = 14;
    private const int MinAvatarId = 1;
    private const int MaxAvatarId = 8;

    private readonly Dictionary<string, IGameRoomModule> _gameModules;

    public GameRoomService(IEnumerable<IGameRoomModule> gameModules)
    {
        _gameModules = gameModules
            .GroupBy(x => GameCatalog.Normalize(x.GameKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

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

            var gameKey = GameCatalog.Normalize(request.GameKey);
            if (!TryGetGameModule(gameKey, out var gameModule))
            {
                return ServiceResult<CreateRoomResponse>.Fail($"ยังไม่รองรับเกม {gameKey}");
            }

            var optionsResult = gameModule.BuildBoardOptionsForCreate(request);
            if (!optionsResult.Success || optionsResult.Value is null)
            {
                return ServiceResult<CreateRoomResponse>.Fail(optionsResult.Error ?? "ไม่สามารถตั้งค่าห้องเกมได้");
            }
            var boardOptions = optionsResult.Value;

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
                GameKey = gameKey,
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

            if (!TryGetGameModule(room.GameKey, out var gameModule))
            {
                return ServiceResult<RoomSnapshot>.Fail($"ยังไม่รองรับเกม {room.GameKey}");
            }

            var validation = gameModule.ValidateRoomBeforeStart(room);
            if (validation is not null)
            {
                return ServiceResult<RoomSnapshot>.Fail(validation);
            }

            gameModule.StartGame(room);
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

            if (!TryGetGameModule(room.GameKey, out var gameModule))
            {
                return ServiceResult<TurnEnvelope>.Fail($"ยังไม่รองรับเกม {room.GameKey}");
            }

            var result = gameModule.ResolveTurn(room, actor, request, isAutoRoll);

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
}
