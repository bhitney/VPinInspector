using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Pinup;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in DEEP collection rule that extends the PinUP game match: for each table
/// present both on disk and in the PinUP Popper database, it compares the values
/// inferred from / parsed out of the table (cGameName as ROM, plus the
/// manufacturer and year parsed from the file name) against what Popper records
/// (ROM, Manufact, GameYear). Mismatches usually mean the database (or the file
/// name) carries stale or inaccurate information.
///
/// Deep because it needs each table's parsed cGameName, so tables must be fully
/// loaded (not shallow).
/// </summary>
public sealed class PinupMetadataCheckRule : ICollectionRule
{
    private readonly PinupMetadataCheckSettings _settings;

    public PinupMetadataCheckRule(PinupMetadataCheckSettings settings) => _settings = settings;

    public string Id => "pinup-metadata-check";

    public string Description => "PinUP metadata check (ROM/manufacturer/year vs database)";

    public bool EnabledByDefault => _settings.Enabled;

    // Deep: relies on each table's parsed cGameName (table.GameName).
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } = new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(CollectionContext context)
    {
        string? folder = context.ResolveFolder();
        if (folder is null)
        {
            yield return Info("requires a folder to scan.");
            yield break;
        }

        if (!File.Exists(_settings.DatabasePath))
        {
            yield return Info($"database not found at '{_settings.DatabasePath}'.");
            yield break;
        }

        List<Finding> findings;
        try
        {
            findings = Evaluate(context, folder);
        }
        catch (Exception ex)
        {
            findings = new List<Finding> { Info($"failed: {ex.Message}") };
        }

        foreach (Finding finding in findings)
        {
            yield return finding;
        }
    }

    private List<Finding> Evaluate(CollectionContext context, string folder)
    {
        var findings = new List<Finding>();

        using PinupDatabase db = PinupDatabase.Open(_settings.DatabasePath);

        IReadOnlyList<PinupEmulator> emulators = db.GetEmulators();
        var emuIds = ResolveEmulatorIds(emulators, folder, _settings);
        if (emuIds.Count == 0)
        {
            findings.Add(Info("no matching emulators (check emulatorIds or folder)."));
            return findings;
        }

        IReadOnlyList<PinupGameMetadata> metadata = db.GetGameMetadata(emuIds);
        if (_settings.VisibleOnly)
        {
            metadata = metadata.Where(m => m.Visible).ToList();
        }

        // Index DB metadata by game file name for a quick per-table lookup. PinUP
        // may store the name with or without the .vpx extension, so key on both.
        var byFile = new Dictionary<string, PinupGameMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (PinupGameMetadata m in metadata)
        {
            if (string.IsNullOrWhiteSpace(m.GameFileName))
            {
                continue;
            }

            byFile.TryAdd(m.GameFileName, m);
            byFile.TryAdd(Path.GetFileNameWithoutExtension(m.GameFileName), m);
        }

        foreach (TableContext tableContext in context.Tables
            .OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase))
        {
            PinballTable table = tableContext.Table;
            string fileName = table.TableName;
            string fileStem = Path.GetFileNameWithoutExtension(fileName);

            if (!byFile.TryGetValue(fileName, out PinupGameMetadata? row) &&
                !byFile.TryGetValue(fileStem, out row))
            {
                continue; // Not registered in the DB; handled by pinup-game-match.
            }

            CompareField(findings, table.TableName, "ROM (cGameName)", table.GameName, row.Rom);
            CompareField(findings, table.TableName, "manufacturer", table.NameInfo.Manufacturer, row.Manufacturer);
            CompareField(
                findings,
                table.TableName,
                "year",
                table.NameInfo.Year?.ToString(),
                row.Year);
        }

        return findings;
    }

    private void CompareField(
        List<Finding> findings,
        string tableName,
        string fieldLabel,
        string? tableValue,
        string? dbValue)
    {
        // Only flag when both sides have a value and they differ. A missing value
        // on either side isn't a mismatch this rule reports (nothing to compare).
        if (string.IsNullOrWhiteSpace(tableValue) || string.IsNullOrWhiteSpace(dbValue))
        {
            return;
        }

        if (string.Equals(tableValue.Trim(), dbValue.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        findings.Add(new Finding(
            Id,
            FindingSeverity.Warning,
            $"{tableName}: {fieldLabel} mismatch — table \"{tableValue.Trim()}\" vs database \"{dbValue.Trim()}\".")
        {
            Details = new Dictionary<string, string>
            {
                ["field"] = fieldLabel,
                ["table"] = tableValue.Trim(),
                ["database"] = dbValue.Trim(),
            },
        });
    }

    private static List<int> ResolveEmulatorIds(
        IReadOnlyList<PinupEmulator> emulators,
        string folder,
        PinupMetadataCheckSettings settings)
    {
        var ids = new HashSet<int>(settings.EmulatorIds);

        if (settings.MatchEmulatorsByFolder)
        {
            string target = NormalizeFolder(folder);
            foreach (PinupEmulator emu in emulators)
            {
                if (!string.IsNullOrWhiteSpace(emu.DirGames) &&
                    string.Equals(NormalizeFolder(emu.DirGames), target, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(emu.EmuId);
                }
            }
        }

        if (settings.VisibleOnly)
        {
            var visibleIds = emulators.Where(e => e.Visible).Select(e => e.EmuId).ToHashSet();
            ids.IntersectWith(visibleIds);
        }

        return ids.OrderBy(i => i).ToList();
    }

    private static string NormalizeFolder(string path) => path.Trim().TrimEnd('\\', '/');

    private Finding Info(string message) =>
        new(Id, FindingSeverity.Info, $"pinup-metadata-check {message}");
}
