using VPin.Inspector.Vpx.Pinup;

namespace VPin.Inspector.Platforms.Vpx.Reporting;

/// <summary>
/// Resolves each scanned table's PinUP Popper visibility status by matching the
/// table's file name (no path) against the Games table's GameFileName column.
/// Loads the whole Games table once (~2k rows) into a case-insensitive map.
/// </summary>
public static class PinupVisibilityLookup
{
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
    /// Builds a map of file name (case-insensitive) to visibility status label by
    /// reading the PinUP Popper Games table. Returns an empty map when the
    /// database file is missing or cannot be read.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Build(string databasePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return result;
        }

        try
        {
            using PinupDatabase db = PinupDatabase.Open(databasePath);
            foreach (KeyValuePair<string, int> entry in db.GetVisibilityByFileName())
            {
                result[entry.Key] = DescribeStatus(entry.Value);
            }
        }
        catch
        {
            // Visibility is a best-effort annotation; ignore database errors.
            return result;
        }

        return result;
    }
}
