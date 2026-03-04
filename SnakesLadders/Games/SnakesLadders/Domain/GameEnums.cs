namespace SnakesLadders.Domain;

public enum DensityMode
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum OverflowMode
{
    StayPut = 0,
    BackByOverflowX2 = 1
}

public enum GameMode
{
    Classic = 0,
    Custom = 1,
    Chaos = 2
}

public enum GameStatus
{
    Waiting = 0,
    Started = 1,
    Finished = 2
}

public enum JumpType
{
    Snake = 0,
    Ladder = 1
}

public enum ForkPathChoice
{
    Safe = 0,
    Risky = 1
}

public enum BoardItemType
{
    RocketBoots = 0,
    MagnetDice = 1,
    SnakeRepellent = 2,
    LadderHack = 3,
    BananaPeel = 4,
    SwapGlove = 5,
    Anchor = 6,
    ChaosButton = 7,
    SnakeRow = 8,
    BridgeToLeader = 9,
    GlobalSnakeRound = 10
}
