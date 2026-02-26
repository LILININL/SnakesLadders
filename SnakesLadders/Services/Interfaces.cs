using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public interface IBoardGenerator
{
    BoardState Generate(BoardOptions options, Random random);
}

public interface IGameEngine
{
    TurnResult ResolveTurn(
        GameRoom room,
        PlayerState player,
        bool useLuckyReroll,
        ForkPathChoice? forkChoice,
        bool isAutoRoll);
}

public interface IGameRoomService
{
    ServiceResult<CreateRoomResponse> CreateRoom(string connectionId, CreateRoomRequest request);
    ServiceResult<JoinRoomResponse> JoinRoom(string connectionId, JoinRoomRequest request);
    ServiceResult<ResumeRoomResponse> ResumeRoom(string connectionId, ResumeRoomRequest request);
    ServiceResult<RoomSnapshot> StartGame(string connectionId, StartGameRequest request);
    ServiceResult<TurnEnvelope> RollDice(string connectionId, RollDiceRequest request, bool isAutoRoll = false);
    ServiceResult<RoomSnapshot> SetReady(string connectionId, SetReadyRequest request);
    ServiceResult<RoomSnapshot> SetAvatar(string connectionId, SetAvatarRequest request);
    ServiceResult<RoomSnapshot> ResetFinishedGame(string roomCode);
    ServiceResult<ChatMessage> SendChat(string connectionId, SendChatRequest request);
    ServiceResult<RoomSnapshot> LeaveRoom(string connectionId, string? roomCode = null);
    ServiceResult<RoomSnapshot> HandleDisconnect(string connectionId);
    ServiceResult<RoomSnapshot> GetRoom(string roomCode);
    IReadOnlyList<PublicRoomSummary> GetPublicRooms();
    void UpsertLobbyPresence(string connectionId, string displayName);
    IReadOnlyList<LobbyOnlineUser> GetLobbyOnlineUsers();
    IReadOnlyList<AutoRollDispatch> ProcessExpiredTurns();
}
