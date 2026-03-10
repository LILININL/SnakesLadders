namespace SnakesLadders.Domain;

public static class MonopolyDefinitions
{
    public static readonly int[] FinalDuelRentBonusPercents = [40, 60, 85, 110, 140, 200];

    public const int DefaultBoardCellCount = 40;
    public const int DefaultStartCash = 1200;
    public const int PassGoCash = 150;
    public const int JailFine = 150;
    public const int JailFineGrowthMultiplier = 2;
    public const int JailCell = 11;
    public const int MaxJailAttempts = 3;
    public const int DefaultHouseSupply = 32;
    public const int DefaultHotelSupply = 12;
    public const int LandmarkCostMultiplier = 2;
    public const int LandmarkRentMultiplier = 170;
    public const double BankLiquidationBaseRatio = 0.3d;
    public const double FinalDuelBankLiquidationBaseRatio = 0.15d;
    public const int NeighborhoodRadius = 2;
    public const double RentAccelerationMultiplier = 1.6d;
    public const double NeighborhoodPrimaryBonus = 0.55d;
    public const double NeighborhoodSecondaryBonus = 0.32d;
    public const double RentGrowthPerCompletedRound = 0.10d;
    public const int SuddenDeathStartRound = 10;
    public const double SuddenDeathExtraRentGrowthPerCompletedRound = 0.08d;
    public const double CityPriceGrowthPerCompletedRound = 0.07d;
    public const double CityPriceGrowthOwnershipThreshold = 0.40d;
    public const int FinalDuelMinimumStartingPlayers = 3;
    public const int FinalDuelDurationRounds = 6;
    public const int FinalDuelOpeningGoReward = 60;
    public const int FinalDuelOpeningGoRounds = 2;
}
