using System.Text.RegularExpressions;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Detects VBScript that overrides a per-frame timer's interval at runtime.
/// The <c>BallShadowUpdate</c> and <c>Graphics</c> timers are meant to fire once
/// per rendered frame, which VPX expresses as an interval of <c>-1</c>. Some
/// tables set the interval to a fixed value in script (for example
/// <c>GraphicsTimer.Interval = 10</c> or <c>BallShadowUpdate.Interval = 10</c>),
/// which decouples updates from the frame rate and causes stutter or extra CPU
/// load.
///
/// Unlike <see cref="GraphicsUpdateTimerRule"/> and the ball-shadow rules, which
/// inspect the timer element's authored property, this rule scans the raw
/// script for an <c>.Interval = &lt;value&gt;</c> assignment. Any value other than
/// <c>-1</c> is flagged. Assignments can appear inside compound statements
/// separated by colons (e.g. <c>x = 1: GraphicsTimer.Interval = 10: Foo()</c>),
/// and commented-out assignments are ignored.
///
/// This is a VPX-specific script scan (the declarative rules.json schema can't
/// express it), so it lives as a code rule rather than a JSON rule. It only
/// flags the issue; there is no automatic fix.
/// </summary>
public sealed partial class TimerOverrideRule : ITableRule
{
    // Matches "<TimerName>.Interval = <value>" where <TimerName> is the
    // BallShadowUpdate timer or a Graphics* timer, capturing the assigned
    // numeric value (which may be negative). Case-insensitive.
    [GeneratedRegex(
        @"\b(?<name>BallShadowUpdate|Graphics\w*)\s*\.\s*Interval\s*=\s*(?<value>-?\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimerIntervalRegex();

    public string Id => "timer-override";

    public string Description =>
        "Flags script that overrides the BallShadowUpdate/Graphics timer interval to a value other than -1.";

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
            // commented-out overrides don't trigger the rule. Compound
            // statements (colon-separated) before the comment are still scanned.
            string line = rawLine;
            int commentIndex = line.IndexOf('\'');
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            foreach (Match match in TimerIntervalRegex().Matches(line))
            {
                string value = match.Groups["value"].Value;
                if (value == "-1")
                {
                    continue;
                }

                string name = match.Groups["name"].Value;
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Script overrides '{name}.Interval' to {value}; the timer should stay at " +
                    "-1 (per-frame). Overriding it decouples updates from the frame rate.");
            }
        }
    }
}
