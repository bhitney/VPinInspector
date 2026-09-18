using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vps;
using VPin.Inspector.Vpx.Pinup;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule ("Version Check"): for each PinUP Popper game that has
/// a VPS <c>WEBGameID</c>, finds the matching VPS <c>puplookup.csv</c> row(s) by
/// that id and compares the local version (Popper GAMEVER) against the newest
/// online version. Games without a WEBGameID are ignored — this rule trusts only
/// the definitive id key, never a fuzzy manufacturer/year/name guess. Version
/// comparison is format-tolerant, so "1.0" and "1.0.0" match. Mismatches are
/// reported in three buckets:
/// <list type="bullet">
/// <item>online (internet/VPS) is newer than local — an update is available;</item>
/// <item>local (PupDatabase) is newer than online — likely an in-development or
/// unreleased table, or a possible error;</item>
/// <item>versions differ but ordering is unknown (dissimilar formats, e.g.
/// "1.1" vs "version2").</item>
/// </list>
/// This is distinct from <c>pup-hygiene</c> (which is about matchability); it
/// assumes matching works and focuses only on version drift.
///
/// The CSV lives in the application directory; when it's missing the rule
/// downloads it on demand via <see cref="VpsDownloader"/>.
/// </summary>
public sealed class VersionCheckRule : ICollectionRule
{
    private static readonly IReadOnlyList<string> Columns =
        new[] { "GameName", "Manufact", "GameYear", "GAMEVER", "WEBGameID" };

    private readonly VersionCheckSettings _settings;
    private readonly string _databasePath;

    public VersionCheckRule(VersionCheckSettings settings, string databasePath)
    {
        _settings = settings;
        _databasePath = databasePath;
    }

    public string Id => "version-check";

    public string Description => "Version check (local Popper GAMEVER vs VPS puplookup.csv)";

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

        if (!File.Exists(_databasePath))
        {
            yield return Info($"database not found at '{_databasePath}'.");
            yield break;
        }

        string csvPath = string.Empty;
        string? csvError = null;
        try
        {
            csvPath = ResolveCsvPath();
        }
        catch (Exception ex)
        {
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

        PupLookupTable lookup = PupLookupTable.Load(csvPath, Columns);
        PupLookupIndex index = PupLookupIndex.Build(lookup);

        using PinupDatabase db = PinupDatabase.Open(_databasePath);

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

        int withWebId = 0;
        int compared = 0;
        int webIdNotInVps = 0;

        // Collect findings into buckets so the summary can present them grouped:
        // VPS newer first (most actionable), then local newer, then unknown.
        var onlineNewer = new List<Finding>();
        var localNewer = new List<Finding>();
        var unknown = new List<Finding>();
        var missingLocal = new List<Finding>();

        foreach (PinupGameIdentity game in games
            .Where(g => !string.IsNullOrWhiteSpace(g.GameName))
            .Where(g => !string.IsNullOrWhiteSpace(g.WebGameId))
            .OrderBy(g => g.GameName, StringComparer.OrdinalIgnoreCase))
        {
            // Only games carrying a VPS WEBGameID are considered; matching is by
            // that id alone (no fuzzy fallback). Everything else is ignored.
            withWebId++;

            PupMatchResult? match = index.MatchByWebGameId(game);
            if (match is null)
            {
                webIdNotInVps++;
                continue;
            }

            // Among the matched rows, pick the newest online version.
            string? onlineVersion = null;
            foreach (PupLookupRow candidate in match.Rows)
            {
                string? version = candidate.Get("GAMEVER");
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                if (onlineVersion is null ||
                    VersionComparer.Compare(onlineVersion, version) == VersionComparison.OnlineNewer)
                {
                    // Compare(local=onlineVersion, online=version): OnlineNewer means
                    // 'version' is newer than the current best, so take it.
                    onlineVersion = version;
                }
            }

            if (onlineVersion is null)
            {
                // Matched by WEBGameID but no versioned VPS row.
                continue;
            }

            compared++;
            string? localVersion = game.Version;

            if (string.IsNullOrWhiteSpace(localVersion))
            {
                missingLocal.Add(new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"\"{game.GameName}\" ({Describe(game.Manufacturer, game.Year)}) — " +
                    $"no local version recorded; VPS has \"{onlineVersion}\"."));
                continue;
            }

            VersionComparison result = VersionComparer.Compare(localVersion, onlineVersion);
            switch (result)
            {
                case VersionComparison.OnlineNewer:
                    onlineNewer.Add(new Finding(
                        Id,
                        FindingSeverity.Warning,
                        $"\"{game.GameName}\" ({Describe(game.Manufacturer, game.Year)}) — " +
                        $"local \"{localVersion}\" vs VPS \"{onlineVersion}\"."));
                    break;

                case VersionComparison.LocalNewer:
                    localNewer.Add(new Finding(
                        Id,
                        FindingSeverity.Info,
                        $"\"{game.GameName}\" ({Describe(game.Manufacturer, game.Year)}) — " +
                        $"local \"{localVersion}\" vs VPS \"{onlineVersion}\"."));
                    break;

                case VersionComparison.Unknown:
                    unknown.Add(new Finding(
                        Id,
                        FindingSeverity.Info,
                        $"\"{game.GameName}\" ({Describe(game.Manufacturer, game.Year)}) — " +
                        $"local \"{localVersion}\" vs VPS \"{onlineVersion}\"."));
                    break;

                case VersionComparison.Equal:
                default:
                    break;
            }
        }

        AppendBucket(findings, "VPS/internet is newer (update available)", onlineNewer);
        AppendBucket(findings, "PupDatabase/local is newer than VPS (in development?)", localNewer);
        AppendBucket(findings, "Version differs (can't tell which is newer)", unknown);
        AppendBucket(findings, "No local version recorded", missingLocal);

        if (withWebId == 0)
        {
            findings.Add(new Finding(
                Id,
                FindingSeverity.Info,
                "No games have a WEBGameID; nothing to version-check."));
            return findings;
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding(
                Id,
                FindingSeverity.Info,
                $"All {compared} WEBGameID-matched game(s) are on the current VPS version."));
        }

        findings.Insert(0, new Finding(
            Id,
            FindingSeverity.Info,
            $"WEBGameID games: {withWebId}; version-compared against VPS: {compared}; " +
            $"WEBGameID not found in VPS: {webIdNotInVps}."));

        return findings;
    }

    /// <summary>
    /// Appends a titled bucket of findings (a header line plus its items) when the
    /// bucket is non-empty, so the summary presents mismatches grouped by kind.
    /// </summary>
    private void AppendBucket(List<Finding> target, string title, List<Finding> bucket)
    {
        if (bucket.Count == 0)
        {
            return;
        }

        FindingSeverity headerSeverity = bucket.Max(f => f.Severity);
        target.Add(new Finding(Id, headerSeverity, $"— {title} ({bucket.Count}) —"));
        target.AddRange(bucket);
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
            downloader.DownloadAllAsync().GetAwaiter().GetResult();
        }

        return path;
    }

    private static string Describe(string? manufacturer, string? year) =>
        $"{manufacturer ?? "?"} {year ?? "?"}";

    private static List<int> ResolveEmulatorIds(
        IReadOnlyList<PinupEmulator> emulators,
        string folder,
        VersionCheckSettings settings)
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
