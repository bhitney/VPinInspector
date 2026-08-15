using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vps;
using VPin.Inspector.Vpx.Pinup;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule ("Pup Hygiene"): cross-checks each PinUP Popper Games
/// entry against the VPS <c>puplookup.csv</c> reference file. A game is
/// considered matched when a CSV row shares the same manufacturer and year
/// (exact) and its GameName fuzzy-matches (see <see cref="PupNameMatcher"/>).
/// Games with no CSV match are reported so the user can reconcile naming.
///
/// The CSV lives in the application directory; when it's missing the rule
/// downloads it on demand via <see cref="VpsDownloader"/>.
/// </summary>
public sealed class PupHygieneRule : ICollectionRule
{
    private const double MatchThreshold = 0.5;

    private readonly PupHygieneSettings _settings;

    public PupHygieneRule(PupHygieneSettings settings) => _settings = settings;

    public string Id => "pup-hygiene";

    public string Description => "PinUP hygiene (Popper games vs VPS puplookup.csv)";

    public bool EnabledByDefault => _settings.Enabled;

    // Quick: compares the database against the CSV reference; no table parse.
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

        if (!File.Exists(_settings.DatabasePath))
        {
            yield return Info($"database not found at '{_settings.DatabasePath}'.");
            yield break;
        }

        string csvPath;
        string? csvError = null;
        try
        {
            csvPath = ResolveCsvPath();
        }
        catch (Exception ex)
        {
            csvPath = string.Empty;
            csvError = ex.Message;
        }

        if (csvError is not null)
        {
            yield return Info($"could not obtain puplookup.csv: {csvError}");
            yield break;
        }

        List<Finding> findings;
        try
        {
            findings = Evaluate(folder, csvPath);
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

    private List<Finding> Evaluate(string folder, string csvPath)
    {
        var findings = new List<Finding>();

        IReadOnlyList<string>? columns =
            _settings.LookupColumns.Count > 0 ? _settings.LookupColumns : null;
        PupLookupTable lookup = PupLookupTable.Load(csvPath, columns);
        PupLookupIndex index = PupLookupIndex.Build(lookup);

        using PinupDatabase db = PinupDatabase.Open(_settings.DatabasePath);

        IReadOnlyList<PinupEmulator> emulators = db.GetEmulators();
        var emuIds = ResolveEmulatorIds(emulators, folder, _settings);
        if (emuIds.Count == 0)
        {
            findings.Add(Info("no matching emulators (check emulatorIds or folder)."));
            return findings;
        }

        IReadOnlyList<PinupGameIdentity> games = db.GetGameIdentities(emuIds);
        if (_settings.VisibleOnly)
        {
            games = games.Where(g => g.Visible).ToList();
        }

        int webIdMatches = 0;

        foreach (PinupGameIdentity game in games
            .Where(g => !string.IsNullOrWhiteSpace(g.GameName))
            .OrderBy(g => g.GameName, StringComparer.OrdinalIgnoreCase))
        {
            PupMatchResult match = index.Match(game, MatchThreshold);

            if (match.Kind == PupMatchKind.WebGameId)
            {
                webIdMatches++;
            }

            if (match.NoCandidates)
            {
                findings.Add(new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"No VPS match (manufacturer/year not found): \"{game.GameName}\" " +
                    $"({Describe(game.Manufacturer, game.Year)})"));
                continue;
            }

            if (!match.Matched)
            {
                findings.Add(new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"No VPS name match: \"{game.GameName}\" ({Describe(game.Manufacturer, game.Year)}); " +
                    (match.ClosestName is null
                        ? "no candidate names for that manufacturer/year."
                        : $"closest was \"{match.ClosestName}\" (score {match.ClosestScore:0.00}).")));
            }
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding(
                Id,
                FindingSeverity.Info,
                $"All {games.Count} game(s) matched a VPS puplookup.csv entry."));
        }

        findings.Insert(0, new Finding(
            Id,
            FindingSeverity.Info,
            $"Matched by WEBGameID: {webIdMatches} of {games.Count} game(s)."));

        return findings;
    }

    /// <summary>
    /// Resolves the CSV path: the configured override, else the app-directory copy
    /// written by <see cref="VpsDownloader"/>. Downloads on demand when missing.
    /// </summary>
    private string ResolveCsvPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LookupCsvPath))
        {
            return _settings.LookupCsvPath;
        }

        var downloader = new VpsDownloader();
        string path = downloader.GetLocalPath("puplookup.csv");
        if (!File.Exists(path))
        {
            // Fetch the reference data synchronously (rule evaluation is sync).
            downloader.DownloadAllAsync().GetAwaiter().GetResult();
        }

        return path;
    }

    private static string Describe(string? manufacturer, string? year) =>
        $"{manufacturer ?? "?"} {year ?? "?"}";

    private static List<int> ResolveEmulatorIds(
        IReadOnlyList<PinupEmulator> emulators,
        string folder,
        PupHygieneSettings settings)
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
