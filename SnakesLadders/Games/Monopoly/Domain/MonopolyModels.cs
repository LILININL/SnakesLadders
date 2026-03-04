namespace SnakesLadders.Domain;

public enum MonopolyCellType
{
    Go = 0,
    Property = 1,
    Railroad = 2,
    Utility = 3,
    Tax = 4,
    Chance = 5,
    CommunityChest = 6,
    Jail = 7,
    FreeParking = 8,
    GoToJail = 9
}

public sealed class MonopolyCellState
{
    public required int Cell { get; init; }
    public required string Name { get; init; }
    public required MonopolyCellType Type { get; init; }
    public string? ColorGroup { get; init; }
    public int Price { get; init; }
    public int Rent { get; init; }
    public int Fee { get; init; }
    public string? OwnerPlayerId { get; set; }
}

public sealed class MonopolyRoomState
{
    public List<MonopolyCellState> Cells { get; } = new();
    public int FreeParkingPot { get; set; }

    public MonopolyCellState? FindCell(int cell) =>
        Cells.FirstOrDefault(x => x.Cell == cell);
}

public static class MonopolyBoardTemplate
{
    public static List<MonopolyCellState> CreateDefaultCells() =>
    [
        Cell(1, "GO", MonopolyCellType.Go),
        Property(2, "Mediterranean Avenue", "Brown", 60, 10),
        Cell(3, "Community Chest", MonopolyCellType.CommunityChest),
        Property(4, "Baltic Avenue", "Brown", 60, 12),
        Tax(5, "Income Tax", 200),
        Property(6, "Reading Railroad", "Railroad", 200, 25, MonopolyCellType.Railroad),
        Property(7, "Oriental Avenue", "Light Blue", 100, 18),
        Cell(8, "Chance", MonopolyCellType.Chance),
        Property(9, "Vermont Avenue", "Light Blue", 100, 20),
        Property(10, "Connecticut Avenue", "Light Blue", 120, 22),
        Cell(11, "Jail / Just Visiting", MonopolyCellType.Jail),
        Property(12, "St. Charles Place", "Pink", 140, 24),
        Property(13, "Electric Company", "Utility", 150, 28, MonopolyCellType.Utility),
        Property(14, "States Avenue", "Pink", 140, 24),
        Property(15, "Virginia Avenue", "Pink", 160, 26),
        Property(16, "Pennsylvania Railroad", "Railroad", 200, 25, MonopolyCellType.Railroad),
        Property(17, "St. James Place", "Orange", 180, 28),
        Cell(18, "Community Chest", MonopolyCellType.CommunityChest),
        Property(19, "Tennessee Avenue", "Orange", 180, 30),
        Property(20, "New York Avenue", "Orange", 200, 32),
        Cell(21, "Free Parking", MonopolyCellType.FreeParking),
        Property(22, "Kentucky Avenue", "Red", 220, 34),
        Cell(23, "Chance", MonopolyCellType.Chance),
        Property(24, "Indiana Avenue", "Red", 220, 36),
        Property(25, "Illinois Avenue", "Red", 240, 38),
        Property(26, "B&O Railroad", "Railroad", 200, 25, MonopolyCellType.Railroad),
        Property(27, "Atlantic Avenue", "Yellow", 260, 40),
        Property(28, "Ventnor Avenue", "Yellow", 260, 42),
        Property(29, "Water Works", "Utility", 150, 28, MonopolyCellType.Utility),
        Property(30, "Marvin Gardens", "Yellow", 280, 44),
        Cell(31, "Go To Jail", MonopolyCellType.GoToJail),
        Property(32, "Pacific Avenue", "Green", 300, 46),
        Property(33, "North Carolina Avenue", "Green", 300, 48),
        Cell(34, "Community Chest", MonopolyCellType.CommunityChest),
        Property(35, "Pennsylvania Avenue", "Green", 320, 50),
        Property(36, "Short Line Railroad", "Railroad", 200, 25, MonopolyCellType.Railroad),
        Cell(37, "Chance", MonopolyCellType.Chance),
        Property(38, "Park Place", "Dark Blue", 350, 60),
        Tax(39, "Luxury Tax", 100),
        Property(40, "Boardwalk", "Dark Blue", 400, 70)
    ];

    private static MonopolyCellState Cell(
        int cell,
        string name,
        MonopolyCellType type) =>
        new()
        {
            Cell = cell,
            Name = name,
            Type = type
        };

    private static MonopolyCellState Property(
        int cell,
        string name,
        string group,
        int price,
        int rent,
        MonopolyCellType type = MonopolyCellType.Property) =>
        new()
        {
            Cell = cell,
            Name = name,
            Type = type,
            ColorGroup = group,
            Price = price,
            Rent = rent
        };

    private static MonopolyCellState Tax(
        int cell,
        string name,
        int fee) =>
        new()
        {
            Cell = cell,
            Name = name,
            Type = MonopolyCellType.Tax,
            Fee = fee
        };
}
