using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Checks that every table has a sidecar <c>.ini</c> settings file next to it
/// (e.g. <c>mytable.vpx</c> -&gt; <c>mytable.ini</c>). Modern VPX stores per-table
/// point-of-view / rendering settings in the <c>.ini</c>; the older <c>.pov</c>
/// file is legacy and is ignored when an <c>.ini</c> is present.
///
/// Three situations are flagged:
/// <list type="bullet">
/// <item>An <c>.ini</c> exists alongside a legacy <c>.pov</c>: the <c>.pov</c> is
/// extraneous (the <c>.ini</c> wins) and can be safely deleted. This is the only
/// auto-fixable case — "fix issues" deletes the stray <c>.pov</c>.</item>
/// <item>Only a legacy <c>.pov</c> exists (no <c>.ini</c>): open the table and
/// save so VPX writes the <c>.ini</c>.</item>
/// <item>Neither an <c>.ini</c> nor a <c>.pov</c> exists: open the table to
/// investigate and save settings.</item>
/// </list>
/// Quick check: it only inspects sibling file names, never the table body.
/// </summary>
public sealed class IniFileCheckRule : ITableRule
{
    public string Id => "ini-file-check";

    public string Description =>
        "Checks the table has a sidecar .ini file (and flags a legacy .pov that can be removed).";

    public bool EnabledByDefault => true;

    // Quick: only reads sibling file names, no table body parse required.
    public AnalysisDepth Depth => AnalysisDepth.Quick;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        string? filePath = context.Table.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            yield break;
        }

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(filePath);
        string iniPath = Path.Combine(directory, baseName + ".ini");
        string povPath = Path.Combine(directory, baseName + ".pov");

        bool hasIni = File.Exists(iniPath);
        bool hasPov = File.Exists(povPath);

        if (hasIni && hasPov)
        {
            // The .ini takes precedence, so the .pov is dead weight. This is the
            // one case the fixer can resolve automatically: delete the .pov.
            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                $"'{context.Table.TableName}' has both an .ini and a legacy .pov file. " +
                "The .ini takes precedence, so the .pov is extraneous and can be deleted.")
            {
                Details = new Dictionary<string, string>
                {
                    ["deleteFile"] = povPath,
                },
            };
            yield break;
        }

        if (!hasIni && hasPov)
        {
            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                $"'{context.Table.TableName}' has a legacy .pov file but no .ini. " +
                "Open the table and save settings so VPX writes the .ini (the .pov format is deprecated).");
            yield break;
        }

        if (!hasIni && !hasPov)
        {
            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                $"'{context.Table.TableName}' has neither an .ini nor a .pov sidecar file (ini needed). " +
                "Open the table and save settings to generate the .ini, then investigate if this was unexpected.");
        }
    }
}
