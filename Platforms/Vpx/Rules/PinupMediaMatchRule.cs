using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Pinup;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule: verifies PinUP Popper media exists for each game
/// registered for the selected emulators, per selected media folder. Ported from
/// the legacy PinupMediaMatchCheck (IConfigurationCheck).
/// </summary>
public sealed class PinupMediaMatchRule : ICollectionRule
{
    private readonly PinupMediaMatchSettings _settings;

    public PinupMediaMatchRule(PinupMediaMatchSettings settings) => _settings = settings;

    public string Id => "media-match";

    public string Description => "PinUP media match (database vs media folders)";

    public bool EnabledByDefault => _settings.Enabled;

    // Quick: compares database entries against media folder listings only.
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

        var mediaFolders = _settings.MediaFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().Trim('\\', '/'))
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mediaFolders.Count == 0)
        {
            yield return Info("no media folders selected.");
            yield break;
        }

        List<Finding> findings;
        try
        {
            findings = Evaluate(folder, mediaFolders);
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

    private List<Finding> Evaluate(string folder, List<string> mediaFolders)
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

        IReadOnlyList<PinupGameMedia> games = db.GetGameMedia(emuIds);
        if (_settings.VisibleOnly)
        {
            games = games.Where(g => g.Visible).ToList();
        }

        games = games
            .Where(g => !string.IsNullOrWhiteSpace(g.GameFileName) &&
                        !string.IsNullOrWhiteSpace(g.MediaDir))
            .ToList();

        foreach (string mediaFolder in mediaFolders)
        {
            foreach (PinupGameMedia game in games
                .Where(g => !MediaExists(g.MediaDir, mediaFolder, g.GameFileName))
                .GroupBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
                .Select(grp => grp.First())
                .OrderBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(
                    Id,
                    FindingSeverity.Error,
                    $"Missing {mediaFolder} media: {StripExtension(game.GameFileName)}.* " +
                        $"(emu {game.EmuId}, \"{game.GameName}\")"));
            }
        }

        return findings;
    }

    private static bool MediaExists(string mediaDir, string mediaFolder, string gameFileName)
    {
        string dir = Path.Combine(mediaDir, mediaFolder);
        if (!Directory.Exists(dir))
        {
            return false;
        }

        string pattern = StripExtension(gameFileName) + ".*";
        return Directory
            .EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
            .Any();
    }

    private static string StripExtension(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName);

    private static List<int> ResolveEmulatorIds(
        IReadOnlyList<PinupEmulator> emulators,
        string folder,
        PinupMediaMatchSettings settings)
    {
        var ids = new HashSet<int>(settings.EmulatorIds);

        if (ids.Count == 0 && settings.MatchEmulatorsByFolder)
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
