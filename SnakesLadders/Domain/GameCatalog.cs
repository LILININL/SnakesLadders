namespace SnakesLadders.Domain;

public static class GameCatalog
{
    public const string SnakesLadders = "snakes-ladders";
    public const string Monopoly = "monopoly";
    public const string DefaultGameKey = SnakesLadders;

    private static readonly HashSet<string> SupportedGameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        SnakesLadders,
        Monopoly
    };

    public static string Normalize(string? gameKey)
    {
        var normalized = (gameKey ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? DefaultGameKey : normalized;
    }

    public static bool IsSupported(string? gameKey)
    {
        return SupportedGameKeys.Contains(Normalize(gameKey));
    }
}
