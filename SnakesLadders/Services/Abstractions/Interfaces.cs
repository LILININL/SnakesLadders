using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public interface IGameRoomModule
{
    string GameKey { get; }
    string DisplayName { get; }
    string Description { get; }
    bool IsAvailable { get; }
    ServiceResult<BoardOptions> BuildBoardOptionsForCreate(CreateRoomRequest request);
    string? ValidateRoomBeforeStart(GameRoom room);
    void StartGame(GameRoom room);
    void ResetFinishedGame(GameRoom room);
    TurnResult ResolveTurn(
        GameRoom room,
        PlayerState player,
        RollDiceRequest request,
        bool isAutoRoll);
    ServiceResult<TurnResult> SubmitGameAction(
        GameRoom room,
        PlayerState actor,
        SubmitGameActionRequest request,
        bool isAutoAction);
}

public interface IGameRoomService
{
    ServiceResult<CreateRoomResponse> CreateRoom(string connectionId, CreateRoomRequest request);
    ServiceResult<JoinRoomResponse> JoinRoom(string connectionId, JoinRoomRequest request);
    ServiceResult<ResumeRoomResponse> ResumeRoom(string connectionId, ResumeRoomRequest request);
    ServiceResult<RoomSnapshot> StartGame(string connectionId, StartGameRequest request);
    ServiceResult<TurnEnvelope> RollDice(string connectionId, RollDiceRequest request, bool isAutoRoll = false);
    ServiceResult<TurnEnvelope> SubmitGameAction(
        string connectionId,
        SubmitGameActionRequest request,
        bool isAutoAction = false);
    ServiceResult<RoomSnapshot> SetReady(string connectionId, SetReadyRequest request);
    ServiceResult<RoomSnapshot> AddBotPlayer(string connectionId, AddBotPlayerRequest request);
    ServiceResult<RoomSnapshot> RemoveBotPlayer(string connectionId, RemoveBotPlayerRequest request);
    ServiceResult<RoomSnapshot> SetFullAuto(string connectionId, SetFullAutoRequest request);
    ServiceResult<RoomSnapshot> VoteFinalDuel(string connectionId, VoteFinalDuelRequest request);
    ServiceResult<RoomSnapshot> SetAvatar(string connectionId, SetAvatarRequest request);
    ServiceResult<RoomSnapshot> ResetFinishedGame(string roomCode);
    ServiceResult<ChatMessage> SendChat(string connectionId, SendChatRequest request);
    ServiceResult<RoomSnapshot> LeaveRoom(string connectionId, string? roomCode = null);
    ServiceResult<RoomSnapshot> HandleDisconnect(string connectionId);
    ServiceResult<RoomSnapshot> GetRoom(string roomCode);
    IReadOnlyList<PublicGameSummary> GetAvailableGames();
    IReadOnlyList<PublicRoomSummary> GetPublicRooms();
    void UpsertLobbyPresence(string connectionId, string displayName);
    IReadOnlyList<LobbyOnlineUser> GetLobbyOnlineUsers();
    IReadOnlyList<AutoActionDispatch> ProcessExpiredActions();
}
