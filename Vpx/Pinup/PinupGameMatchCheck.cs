using VPX_Inspector.Vpx.Checks;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx.Pinup;

/// <summary>
/// Configuration check: compares the .vpx files in the scanned folder against the
/// games registered in the PinUP Popper database for a set of emulators.
/// - Games in the DB but missing on disk are errors (Popper thinks they exist).
/// - Files on disk not in the DB are informational (not registered yet).
/// </summary>
public sealed class PinupGameMatchCheck : IConfigurationCheck
{
    private readonly PinupMatchSettings _settings;

    public PinupGameMatchCheck(PinupMatchSettings settings) => _settings = settings;

    public string Id => "pinup-game-match";

    public string Description => "PinUP game match (database vs tables folder)";

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

            IReadOnlyList<PinupGame> games = db.GetGames(emuIds);
            if (_settings.VisibleOnly)
            {
                games = games.Where(g => g.Visible).ToList();
            }

            // Files on disk. Config checks default to the RAW filesystem; the user
            // can opt in to honoring excludes via respectExcludePatterns.
            var diskFiles = context.EnumerateVpxFileNames(folder, _settings.RespectExcludePatterns);
            var diskSet = new HashSet<string>(diskFiles, StringComparer.OrdinalIgnoreCase);
            var dbSet = new HashSet<string>(
                games.Select(g => g.GameFileName).Where(f => !string.IsNullOrWhiteSpace(f)),
                StringComparer.OrdinalIgnoreCase);

            var missingOnDisk = games
                .Where(g => !string.IsNullOrWhiteSpace(g.GameFileName) && !diskSet.Contains(g.GameFileName))
                .GroupBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
                .Select(grp => grp.First())
                .OrderBy(g => g.GameFileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"[!] {g.GameFileName}   (emu {g.EmuId}, \"{g.GameName}\")")
                .ToList();

            var missingInDb = diskFiles
                .Where(f => !dbSet.Contains(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => $"[ ] {f}")
                .ToList();

            return new ConfigurationCheckResult
            {
                CheckId = Id,
                Title = "PINUP GAME MATCH",
                Ran = true,
                SummaryLines = new[]
                {
                    $"Folder:     {folder}",
                    $"Emulators:  {string.Join(", ", emuIds)}",
                    $"Database games: {dbSet.Count}, on disk: {diskSet.Count}" +
                        (_settings.RespectExcludePatterns ? " (excludes honored)" : ""),
                },
                Sections = new[]
                {
                    new CheckSection
                    {
                        Title = "In database but MISSING on disk",
                        Severity = CheckSeverity.Error,
                        Lines = missingOnDisk,
                    },
                    new CheckSection
                    {
                        Title = "On disk but NOT in database",
                        Severity = CheckSeverity.Info,
                        Lines = missingInDb,
                    },
                },
            };
        }
        catch (Exception ex)
        {
            return ConfigurationCheckResult.Skip(Id, Description, $"failed: {ex.Message}");
        }
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
}
