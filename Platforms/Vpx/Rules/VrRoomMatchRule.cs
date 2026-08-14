using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule: every "VR ROOM &lt;name&gt;.vpx" should have a matching
/// non-VR "&lt;name&gt;.vpx" in the same folder. A VR ROOM without its base table is
/// unusual and worth a warning. Ported from the legacy VrRoomMatchCheck.
/// </summary>
public sealed class VrRoomMatchRule : ICollectionRule
{
    private readonly VrRoomMatchSettings _settings;

    public VrRoomMatchRule(VrRoomMatchSettings settings) => _settings = settings;

    public string Id => "vr-room-matching";

    public string Description => "VR ROOM files should have a matching non-VR table";

    public bool EnabledByDefault => _settings.Enabled;

    // Quick: only compares file names within the folder.
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

        string prefix = _settings.Prefix;
        if (string.IsNullOrEmpty(prefix))
        {
            yield return Info("no VR ROOM prefix configured.");
            yield break;
        }

        var files = context.EnumerateVpxFileNames(folder, _settings.RespectExcludePatterns);
        var baseNames = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        var vrRoomFiles = files
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (string vr in vrRoomFiles)
        {
            string expectedBase = vr.Substring(prefix.Length);
            if (!baseNames.Contains(expectedBase))
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"VR ROOM missing a non-VR table: {vr} (expected base: {expectedBase})");
            }
        }
    }

    private Finding Info(string message) => new(Id, FindingSeverity.Info, $"skipped: {message}");
}
