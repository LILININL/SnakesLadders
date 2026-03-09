namespace SnakesLadders.Domain;

public static class MonopolyDefinitions
{
    public const int DefaultBoardCellCount = 40;
    public const int DefaultStartCash = 1500;
    public const int PassGoCash = 200;
    public const int JailFine = 150;
    public const int JailFineGrowthMultiplier = 2;
    public const int JailCell = 11;
    public const int MaxJailAttempts = 3;
    public const int DefaultHouseSupply = 32;
    public const int DefaultHotelSupply = 12;
    public const int LandmarkCostMultiplier = 2;
    public const int LandmarkRentMultiplier = 170;
    public const double BankLiquidationBaseRatio = 0.5d;
    public const int NeighborhoodRadius = 2;
    public const double RentAccelerationMultiplier = 1.6d;
    public const double NeighborhoodPrimaryBonus = 0.55d;
    public const double NeighborhoodSecondaryBonus = 0.32d;
    public const double RentGrowthPerCompletedRound = 0.10d;
}
