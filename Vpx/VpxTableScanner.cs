namespace VPX_Inspector.Vpx;

/// <summary>
/// Scans the GameStg storage of an extracted VPX file (7-zip style extraction)
/// and returns the parsed elements, with helpers to inspect timers.
/// </summary>
public static class VpxTableScanner
{
    /// <summary>
    /// Parses every GameItem stream found under the extracted table directory.
    /// </summary>
    /// <param name="extractedTablePath">
    /// The root folder produced by extracting the .vpx (contains GameStg / TableInfo).
    /// </param>
    public static IReadOnlyList<GameItem> ScanGameItems(string extractedTablePath)
    {
        string gameStg = ResolveGameStgPath(extractedTablePath);

        var items = new List<GameItem>();
        if (!Directory.Exists(gameStg))
        {
            return items;
        }

        foreach (var file in Directory.EnumerateFiles(gameStg, "GameItem*"))
        {
            byte[] bytes = File.ReadAllBytes(file);
            items.Add(GameItem.Parse(Path.GetFileName(file), bytes));
        }

        return items;
    }

    /// <summary>
    /// Returns all elements that carry a timer, ordered by shortest interval first
    /// (the ones most likely to hurt performance).
    /// </summary>
    public static IReadOnlyList<GameItem> FindTimers(string extractedTablePath) =>
        ScanGameItems(extractedTablePath)
            .Where(i => i.HasTimer)
            .OrderBy(i => i.TimerIntervalMs)
            .ToList();

    /// <summary>
    /// Finds a specific element by (case-insensitive) name, e.g. "BallShadowUpdate".
    /// </summary>
    public static GameItem? FindByName(string extractedTablePath, string name) =>
        ScanGameItems(extractedTablePath)
            .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string ResolveGameStgPath(string extractedTablePath)
    {
        // Accept either the table root or the GameStg folder itself.
        if (string.Equals(Path.GetFileName(extractedTablePath), "GameStg", StringComparison.OrdinalIgnoreCase))
        {
            return extractedTablePath;
        }

        return Path.Combine(extractedTablePath, "GameStg");
    }
}
