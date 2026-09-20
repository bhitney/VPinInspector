using VPin.Inspector.Vpx.Pinup;

namespace VPin.Inspector.Platforms.Vpx.Reporting;

/// <summary>
/// A scanned table's cross-referenced PinUP Popper info: the raw visibility code
/// (0 = Disabled, 1 = Visible, 2 = Mature, 3 = WIP; -1 when not matched), the
/// human-readable status label, and the game rating (0-5, or null when
/// missing/empty).
/// </summary>
public sealed record PinupCrossRef(int Visibility, string StatusLabel, int? Rating);

/// <summary>
/// Resolves each scanned table's PinUP Popper visibility status and rating by
/// matching the table's file name (no path) against the Games table's
/// GameFileName column. Loads the whole Games table once (~2k rows) into a
/// case-insensitive map.
/// </summary>
public static class PinupVisibilityLookup
{
    /// <summary>Raw visibility code used when a table is not matched in the Games table.</summary>
    public const int UnknownVisibility = -1;

    /// <summary>Human-readable label for a raw PinUP visibility code.</summary>
    public static string DescribeStatus(int visibility) => visibility switch
    {
        0 => "Disabled",
        1 => "Visible",
        2 => "Mature",
        3 => "WIP",
        _ => "Unknown",
    };

    /// <summary>
    /// Builds a map of file name (case-insensitive) to cross-referenced PinUP
    /// info (visibility status + rating) by reading the PinUP Popper Games table.
    /// Returns an empty map when the database file is missing or cannot be read.
    /// </summary>
    public static IReadOnlyDictionary<string, PinupCrossRef> Build(string databasePath)
    {
        var result = new Dictionary<string, PinupCrossRef>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return result;
        }

        try
        {
            using PinupDatabase db = PinupDatabase.Open(databasePath);
            foreach (KeyValuePair<string, PinupGameInfo> entry in db.GetGameInfoByFileName())
            {
                result[entry.Key] = new PinupCrossRef(
                    entry.Value.Visibility,
                    DescribeStatus(entry.Value.Visibility),
                    entry.Value.Rating);
            }
        }
        catch
        {
            // Cross-reference is a best-effort annotation; ignore database errors.
            return result;
        }

        return result;
    }
}
