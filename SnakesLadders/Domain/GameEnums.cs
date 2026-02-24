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
