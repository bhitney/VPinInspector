using VPX_Inspector.Vpx.Checks;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx.VrRoom;

/// <summary>
/// Configuration check: every "VR ROOM &lt;name&gt;.vpx" file should have a matching
/// non-VR "&lt;name&gt;.vpx" in the same folder. By convention a VR ROOM file is the
/// VR variant of an existing table; a VR ROOM without its base table usually means
/// the table is VR-only, which is unusual and worth a warning.
/// </summary>
public sealed class VrRoomMatchCheck : IConfigurationCheck
{
    private readonly VrRoomMatchSettings _settings;

    public VrRoomMatchCheck(VrRoomMatchSettings settings) => _settings = settings;

    public string Id => "vr-room-matching";

    public string Description => "VR ROOM files should have a matching non-VR table";

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

        string prefix = _settings.Prefix;
        if (string.IsNullOrEmpty(prefix))
        {
            return ConfigurationCheckResult.Skip(Id, Description, "no VR ROOM prefix configured.");
        }

        // VR ROOM detection needs the real filesystem; respect the per-check opt-in.
        var files = context.EnumerateVpxFileNames(folder, _settings.RespectExcludePatterns);

        // Base table names present on disk (case-insensitive), for pairing.
        var baseNames = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        var vrRoomFiles = files
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orphans = new List<string>();
        foreach (string vr in vrRoomFiles)
        {
            // Strip the prefix to get the expected base file name.
            string expectedBase = vr.Substring(prefix.Length);
            if (!baseNames.Contains(expectedBase))
            {
                orphans.Add($"[!] {vr}   (expected base: {expectedBase})");
            }
        }

        return new ConfigurationCheckResult
        {
            CheckId = Id,
            Title = "VR ROOM MATCHING",
            Ran = true,
            SummaryLines = new[]
            {
                $"Folder:       {folder}",
                $"VR ROOM files: {vrRoomFiles.Count}" +
                    (_settings.RespectExcludePatterns ? " (excludes honored)" : ""),
            },
            Sections = new[]
            {
                new CheckSection
                {
                    Title = "VR ROOM files missing a non-VR table",
                    Severity = CheckSeverity.Warning,
                    Lines = orphans,
                },
            },
        };
    }
}
