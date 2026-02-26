namespace SnakesLadders.Domain;

public sealed class RuleOptions
{
    public bool CheckpointShieldEnabled { get; set; } = true;
    public int CheckpointInterval { get; set; } = 50;

    public bool ComebackBoostEnabled { get; set; } = true;

    public bool LuckyRerollEnabled { get; set; } = true;
    public int LuckyRerollPerPlayer { get; set; } = 2;

    public bool ForkPathEnabled { get; set; } = true;

    public bool SnakeFrenzyEnabled { get; set; } = true;
    public int SnakeFrenzyIntervalTurns { get; set; } = 5;

    public bool MercyLadderEnabled { get; set; } = true;
    public int MercyLadderBoost { get; set; } = 12;

    public bool TurnTimerEnabled { get; set; } = true;
    public int TurnSeconds { get; set; } = 15;

    public bool RoundLimitEnabled { get; set; } = true;
    public int MaxRounds { get; set; } = 80;

    public bool MarathonSpeedupEnabled { get; set; } = true;
    public int MarathonThreshold { get; set; } = 300;
    public double MarathonLadderMultiplier { get; set; } = 1.2d;
}

public sealed class BoardOptions
{
    private const int MinBoardSize = 50;
    private const int DefaultBoardSize = 100;
    private const int TechnicalCap = 5000;

    public GameMode GameMode { get; set; } = GameMode.Custom;
    public int BoardSize { get; set; } = DefaultBoardSize;
    public DensityMode DensityMode { get; set; } = DensityMode.Medium;
    public OverflowMode OverflowMode { get; set; } = OverflowMode.StayPut;
    public int? Seed { get; set; }
    public RuleOptions RuleOptions { get; set; } = new();

    public void Normalize()
    {
        if (!Enum.IsDefined(GameMode))
        {
            GameMode = GameMode.Custom;
        }

        if (BoardSize <= 0)
        {
            BoardSize = DefaultBoardSize;
        }

        if (RuleOptions.CheckpointInterval <= 0)
        {
            RuleOptions.CheckpointInterval = 50;
        }

        if (RuleOptions.LuckyRerollPerPlayer < 0)
        {
            RuleOptions.LuckyRerollPerPlayer = 0;
        }

        if (RuleOptions.SnakeFrenzyIntervalTurns <= 0)
        {
            RuleOptions.SnakeFrenzyIntervalTurns = 5;
        }

        if (RuleOptions.MercyLadderBoost <= 0)
        {
            RuleOptions.MercyLadderBoost = 12;
        }

        if (RuleOptions.TurnSeconds <= 0)
        {
            RuleOptions.TurnSeconds = 15;
        }

        if (RuleOptions.MaxRounds <= 0)
        {
            RuleOptions.MaxRounds = 80;
        }

        if (RuleOptions.MarathonThreshold < MinBoardSize)
        {
            RuleOptions.MarathonThreshold = MinBoardSize;
        }

        if (RuleOptions.MarathonLadderMultiplier < 1.0d)
        {
            RuleOptions.MarathonLadderMultiplier = 1.0d;
        }

        if (GameMode == GameMode.Classic)
        {
            RuleOptions.CheckpointShieldEnabled = false;
            RuleOptions.ComebackBoostEnabled = false;
            RuleOptions.LuckyRerollEnabled = false;
            RuleOptions.LuckyRerollPerPlayer = 0;
            RuleOptions.ForkPathEnabled = false;
            RuleOptions.SnakeFrenzyEnabled = false;
            RuleOptions.MercyLadderEnabled = false;
            RuleOptions.TurnTimerEnabled = false;
            RuleOptions.RoundLimitEnabled = false;
            RuleOptions.MarathonSpeedupEnabled = false;
        }
    }

    public string? Validate()
    {
        return BoardSize switch
        {
            < MinBoardSize => $"ขนาดกระดานต้องไม่น้อยกว่า {MinBoardSize} ช่อง",
            > TechnicalCap => $"ขนาดกระดานเกินขีดจำกัดระบบ ({TechnicalCap})",
            _ => null
        };
    }
}
