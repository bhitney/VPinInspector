using VPX_Inspector.Vpx.Checks;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx.Pinup;

/// <summary>
/// Configuration check: verifies that PinUP Popper media exists for each game
/// registered for the selected emulators. For every game, Popper looks in the
/// emulator's DirMedia (or the GlobalSettings GlobalMediaDir fallback), inside a
/// per-type subfolder (Playfield, Topper, BackGlass, DMD, Loading, Menu), for a
/// file named after the game file with any extension (a video or an image).
/// Each media folder is checked independently so the user can target just one
/// media type (e.g. only missing toppers).
/// </summary>
public sealed class PinupMediaMatchCheck : IConfigurationCheck
{
    private readonly PinupMediaMatchSettings _settings;

    public PinupMediaMatchCheck(PinupMediaMatchSettings settings) => _settings = settings;

    public string Id => "media-match";

    public string Description => "PinUP media match (database vs media folders)";

    public bool Enabled => _settings.Enabled;

    public ConfigurationCheckResult Run(ConfigurationCheckContext context)
    {
        if (!context.IsFullScan)
        {
            return ConfigurationCheckResult.Skip(Id, Description, "requires a full folder scan.");
        }

        string? folder = context.ResolveFolder();
        if (folder is null)
        {
            return ConfigurationCheckResult.Skip(Id, Description, "requires a folder to scan.");
        }

        if (!File.Exists(_settings.DatabasePath))
        {
            return ConfigurationCheckResult.Skip(
                Id, Description, $"database not found at '{_settings.DatabasePath}'.");
        }

        var mediaFolders = _settings.MediaFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().Trim('\\', '/'))
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mediaFolders.Count == 0)
        {
            return ConfigurationCheckResult.Skip(Id, Description, "no media folders selected.");
        }

        try
        {
            using PinupDatabase db = PinupDatabase.Open(_settings.DatabasePath);

            IReadOnlyList<PinupEmulator> emulators = db.GetEmulators();
            var emuIds = ResolveEmulatorIds(emulators, folder, _settings);
            if (emuIds.Count == 0)
            {
                return ConfigurationCheckResult.Skip(
                    Id, Description, "no matching emulators (check emulatorIds or folder).");
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

            var sections = new List<CheckSection>();
            foreach (string mediaFolder in mediaFolders)
            {
                var missing = games
                    .Where(g => !MediaExists(g.MediaDir, mediaFolder, g.GameFileName))
                    .GroupBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(grp => grp.First())
                    .OrderBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => $"[!] {StripExtension(g.GameFileName)}.*   (emu {g.EmuId}, \"{g.GameName}\")")
                    .ToList();

                sections.Add(new CheckSection
                {
                    Title = $"Missing {mediaFolder} media",
                    Severity = CheckSeverity.Error,
                    Lines = missing,
                });
            }

            return new ConfigurationCheckResult
            {
                CheckId = Id,
                Title = "PINUP MEDIA MATCH",
                Ran = true,
                SummaryLines = new[]
                {
                    $"Emulators:  {string.Join(", ", emuIds)}",
                    $"Media folders: {string.Join(", ", mediaFolders)}",
                    $"Games checked: {games.Count}",
                },
                Sections = sections,
            };
        }
        catch (Exception ex)
        {
            return ConfigurationCheckResult.Skip(Id, Description, $"failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True when at least one file named after the game (any extension) exists in
    /// the given media type subfolder under the resolved media directory.
    /// </summary>
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

        // Explicit emulator IDs take precedence: when the user supplies any, use
        // exactly those so the whole Popper database can be checked regardless of
        // where the source .vpx files sit. Folder matching only kicks in as the
        // default (empty emulatorIds), picking the emulator(s) that use this
        // tables folder.
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
}
