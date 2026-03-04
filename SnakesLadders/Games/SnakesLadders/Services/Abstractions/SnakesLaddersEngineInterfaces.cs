using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public interface IBoardGenerator
{
    BoardState Generate(BoardOptions options, Random random);
}

public interface IGameEngine
{
    void SeedRoomState(GameRoom room);

    TurnResult ResolveTurn(
        GameRoom room,
        PlayerState player,
        bool useLuckyReroll,
        ForkPathChoice? forkChoice,
        bool isAutoRoll);
}
