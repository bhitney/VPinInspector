using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags tables whose file/display name doesn't follow the expected
/// "Name (Manufacturer Year)" convention, e.g. "Addams Family (Bally 1992).vpx".
/// A well-formed name is what lets us match a table to an online database entry,
/// so this rule is the front line for that matching. Platform-agnostic: any
/// emulator benefits from a consistent naming convention.
/// </summary>
public sealed class WellFormedNameRule : ITableRule
{
    public string Id => "well-formed-name";

    public string Description =>
        "Checks the table name follows 'Name (Manufacturer Year)' (optionally with a trailing PUP).";

    public bool EnabledByDefault => true;

    // Quick: only reads the table's file name, no table body parse required.
    public AnalysisDepth Depth => AnalysisDepth.Quick;

    public IReadOnlySet<string> SupportedPlatforms { get; } = new HashSet<string>();

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        TableNameInfo info = context.Table.NameInfo;
        if (info.IsWellFormed)
        {
            yield break;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(info.Name))
        {
            missing.Add("name");
        }

        if (string.IsNullOrWhiteSpace(info.Manufacturer))
        {
            missing.Add("manufacturer");
        }

        if (info.Year is null)
        {
            missing.Add("year");
        }

        // A side-car VR ROOM file must share the exact name of its base table.
        // If the base name is flagged here, the VR ROOM copy has to be renamed
        // in lockstep, so surface it prominently.
        bool hasVrRoomSibling = HasVrRoomSibling(context.Table.FilePath);
        string prefix = hasVrRoomSibling ? "[VR ROOM FOUND!] " : string.Empty;

        yield return new Finding(
            Id,
            FindingSeverity.Warning,
            $"{prefix}'{context.Table.TableName}' isn't a well-formed table name " +
            $"(missing/unparseable: {string.Join(", ", missing)}). " +
            "Expected 'Name (Manufacturer Year)'.")
        {
            Details = new Dictionary<string, string>
            {
                ["name"] = info.Name,
                ["manufacturer"] = info.Manufacturer ?? string.Empty,
                ["year"] = info.Year?.ToString() ?? string.Empty,
                ["pup"] = info.IsPup ? "true" : "false",
                ["vrRoomSibling"] = hasVrRoomSibling ? "true" : "false",
            },
        };
    }

    /// <summary>
    /// Returns true when a "VR ROOM &lt;file&gt;" side-car exists next to the
    /// table (e.g. "VR ROOM MyTable.vpx" beside "MyTable.vpx"). The VR ROOM copy
    /// must always share the base table's name, so it has to be renamed too.
    /// </summary>
    private static bool HasVrRoomSibling(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(filePath);
        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // Don't flag the VR ROOM file against itself.
        if (fileName.StartsWith("VR ROOM ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string siblingPath = Path.Combine(directory ?? string.Empty, $"VR ROOM {fileName}");
        return File.Exists(siblingPath);
    }
}
