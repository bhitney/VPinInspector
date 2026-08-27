using System.Text.RegularExpressions;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Detects use of "ninuzzu's ball shadow" routine in a table's VBScript. The
/// routine is authored as a commented section header that typically looks like:
/// <code>'	ninuzzu's	BALL SHADOW</code>
/// A common improved/modified variant reads:
/// <code>'	ninuzzu's	BALL SHADOW - MODIFIED (bthLonewolf)</code>
///
/// The rule flags the original (unmodified) routine so it can be upgraded. A
/// line that also contains "MODIFIED" is treated as the improved variant and is
/// not flagged. This is a VPX-specific script scan (it parses the raw script
/// text, which the declarative rules.json schema can't express), so it lives as
/// a code rule rather than a JSON rule.
/// </summary>
public sealed partial class BallShadowRoutineRule : ITableRule
{
    // Matches a line mentioning both "ninuzzu" and "shadow" (in that order,
    // tolerant of the tabs/spacing in the header comment), case-insensitive.
    [GeneratedRegex(
        @"ninuzzu.*shadow",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BallShadowHeaderRegex();

    public string Id => "ball-shadow-routine";

    public string Description =>
        "Detects the original (unmodified) 'ninuzzu's ball shadow' script routine.";

    public bool EnabledByDefault => true;

    // Deep: needs the parsed table script.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        string? script = context.Table.Script;
        if (string.IsNullOrEmpty(script))
        {
            yield break;
        }

        bool foundOriginal = false;
        foreach (string line in script.Split('\n'))
        {
            if (!BallShadowHeaderRegex().IsMatch(line))
            {
                continue;
            }

            // The improved/modified variant is already the recommended one; if
            // it's present anywhere, don't flag the table at all.
            if (line.IndexOf("MODIFIED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield break;
            }

            foundOriginal = true;
        }

        if (foundOriginal)
        {
            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                "Table uses the original 'ninuzzu's ball shadow' routine; " +
                "consider replacing it with the improved (MODIFIED) version.");
        }
    }
}
