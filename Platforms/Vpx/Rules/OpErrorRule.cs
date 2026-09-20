using System.Text.RegularExpressions;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Detects a <c>B2SSetData</c> call that sets a backglass data value in the
/// 100-199 range to <c>2</c> (for example <c>B2SSetData 104, 2</c> or, with a
/// controller prefix, <c>Controller.B2SSetData 104,2</c>). These specific
/// calls were introduced during manual table fixes and need to be corrected by
/// hand, so this rule flags them for review.
///
/// The first argument must be a three-digit number in the 100-199 range and the
/// second argument must be exactly <c>2</c>; a call such as
/// <c>B2SSetData 101, 1</c> is fine and not flagged. The space after the comma
/// is optional. Assignments can appear inside compound statements separated by
/// colons (e.g. <c>var = 123 : B2SSetData 101, 2</c>), and commented-out calls
/// are ignored.
///
/// This is a VPX-specific script scan (the declarative rules.json schema can't
/// express it), so it lives as a code rule rather than a JSON rule. It only
/// flags the issue; there is no automatic fix.
/// </summary>
public sealed partial class OpErrorRule : ITableRule
{
    // Matches "B2SSetData 1nn, 2" where 1nn is a 100-199 number and the second
    // argument is exactly 2. An optional controller-style prefix (e.g.
    // "Controller.") is allowed. The space after the comma is optional.
    // Case-insensitive.
    [GeneratedRegex(
        @"\bB2SSetData\s+(?<data>1\d{2})\s*,\s*2\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex B2SSetDataRegex();

    public string Id => "op-error";

    public string Description =>
        "Flags B2SSetData calls that set a 100-199 value to 2, which were introduced during manual fixes and must be corrected by hand.";

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

        foreach (string rawLine in script.Split('\n'))
        {
            // Ignore anything from the first VBScript comment marker onward so
            // commented-out calls don't trigger the rule. Compound statements
            // (colon-separated) before the comment are still scanned.
            string line = rawLine;
            int commentIndex = line.IndexOf('\'');
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            foreach (Match match in B2SSetDataRegex().Matches(line))
            {
                string data = match.Groups["data"].Value;
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Script calls 'B2SSetData {data}, 2'; this value was introduced during a manual " +
                    "fix and needs to be corrected by hand.");
            }
        }
    }
}
