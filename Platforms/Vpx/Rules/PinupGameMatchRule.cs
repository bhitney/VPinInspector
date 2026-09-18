using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Pinup;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule: compares .vpx files in the scanned folder against the
/// games registered in the PinUP Popper database for a set of emulators.
/// - Games in the DB but missing on disk are errors (Popper thinks they exist).
/// - Files on disk not in the DB are informational (not registered yet).
/// Ported from the legacy PinupGameMatchCheck (IConfigurationCheck).
/// </summary>
public sealed class PinupGameMatchRule : ICollectionRule
{
    private readonly PinupMatchSettings _settings;
    private readonly string _databasePath;

    public PinupGameMatchRule(PinupMatchSettings settings, string databasePath)
    {
        _settings = settings;
        _databasePath = databasePath;
    }

    public string Id => "pinup-game-match";

    public string Description => "PinUP game match (database vs tables folder)";

    public bool EnabledByDefault => _settings.Enabled;

    // Quick: compares folder file names against the database only.
    public AnalysisDepth Depth => AnalysisDepth.Quick;

    public IReadOnlySet<string> SupportedPlatforms { get; } = new HashSet<string>();

    public IEnumerable<Finding> Evaluate(CollectionContext context)
    {
        string? folder = context.ResolveFolder();
        if (folder is null)
        {
            yield return Info("requires a folder to scan.");
            yield break;
        }

        if (!File.Exists(_databasePath))
        {
            yield return Info($"database not found at '{_databasePath}'.");
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

        using PinupDatabase db = PinupDatabase.Open(_databasePath);

        IReadOnlyList<PinupEmulator> emulators = db.GetEmulators();
        var emuIds = ResolveEmulatorIds(emulators, folder, _settings);
        if (emuIds.Count == 0)
        {
            findings.Add(Info("no matching emulators (check emulatorIds or folder)."));
            return findings;
        }

        IReadOnlyList<PinupGame> games = db.GetGames(emuIds);
        if (_settings.VisibleOnly)
        {
            games = games.Where(g => g.Visible).ToList();
        }

        var diskFiles = context.EnumerateVpxFileNames(folder, _settings.RespectExcludePatterns);
        var diskSet = new HashSet<string>(diskFiles, StringComparer.OrdinalIgnoreCase);
        var dbSet = new HashSet<string>(
            games.Select(g => g.GameFileName).Where(f => !string.IsNullOrWhiteSpace(f)),
            StringComparer.OrdinalIgnoreCase);

        foreach (PinupGame game in games
            .Where(g => !string.IsNullOrWhiteSpace(g.GameFileName) && !diskSet.Contains(g.GameFileName))
            .GroupBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
            .Select(grp => grp.First())
            .OrderBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(
                Id,
                FindingSeverity.Error,
                $"In database but MISSING on disk: {game.GameFileName} (emu {game.EmuId}, \"{game.GameName}\")"));
        }

        foreach (string file in diskFiles
            .Where(f => !dbSet.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(
                Id,
                FindingSeverity.Info,
                $"On disk but NOT in database: {file}"));
        }

        return findings;
    }

    private static List<int> ResolveEmulatorIds(
        IReadOnlyList<PinupEmulator> emulators,
        string folder,
        PinupMatchSettings settings)
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

    private Finding Info(string message) => new(Id, FindingSeverity.Info, $"skipped: {message}");
}
