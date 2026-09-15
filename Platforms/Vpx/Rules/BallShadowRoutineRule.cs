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
/// The routine is often tweaked per-table, so relying on the header alone
/// misses tables. In addition to the header, this rule keys off two more
/// stable signals: the <c>Sub BallShadowUpdate_timer()</c> declaration, and the
/// characteristic shadow-position line, e.g.
/// <code>BallShadow(b).X = ((BOT(b).X) - (Ballsize/6) + ((BOT(b).X - (Table1.Width/2))/7)) + 6</code>
/// whose trailing offset (<c>+ 6</c>, <c>+ 10</c>, <c>+ 2</c>, or commented out)
/// varies between tables.
///
/// The rule flags the original (unmodified) routine so it can be upgraded. If
/// the modification marker <c>BALL SHADOW - MODIFIED (bthLonewolf)</c> appears
/// anywhere in the script, the table has already been upgraded and is not
/// flagged. Tables that use the more advanced VPW "dynamic ball shadows"
/// system (identified by the <c>DynamicBallShadowsOn</c> constant, even when
/// commented out or disabled) are also skipped, since they don't use the
/// simple ninuzzu routine. This is a
/// VPX-specific script scan (it parses the raw script text, which the
/// declarative rules.json schema can't express), so it lives as a code rule
/// rather than a JSON rule.
/// </summary>
public sealed partial class BallShadowRoutineRule : ITableRule
{
    // Matches a section-header comment line whose comment text begins with an
    // optional "ninuzzu's" attribution followed by "ball shadow". The line must
    // be a comment (leading ' with optional whitespace/decoration), which
    // avoids matching descriptive prose that merely mentions "ninuzzu" or
    // "ball shadow" mid-sentence. Case-insensitive, per-line via Multiline.
    [GeneratedRegex(
        @"^\s*'[\s*]*(ninuzzu'?s\s+)?ball\s*shadow\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BallShadowHeaderRegex();

    // Matches the (fairly consistent) timer sub declaration.
    [GeneratedRegex(
        @"\bSub\s+BallShadowUpdate_timer\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BallShadowTimerSubRegex();

    // Matches the characteristic shadow-position line, ignoring whitespace and
    // the variable trailing offset. Works even when the whole line is commented.
    [GeneratedRegex(
        @"BallShadow\(b\)\.X\s*=.*BOT\(b\)\.X.*Ballsize\s*/\s*6.*Table1\.Width\s*/\s*2.*/\s*7",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BallShadowPositionRegex();

    // Matches the VPW "dynamic ball shadows" constant. Its presence (even when
    // commented out or set to 0) indicates the more advanced shadow system
    // rather than the simple ninuzzu routine, so the table is not flagged.
    [GeneratedRegex(
        @"DynamicBallShadowsOn",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DynamicBallShadowsRegex();

    // The modification marker that indicates the routine was already upgraded.
    private const string ModifiedMarker = "BALL SHADOW - MODIFIED (bthLonewolf)";

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

        // If the table already carries the modification marker anywhere, it has
        // been upgraded; don't flag it regardless of the other signals.
        if (script.IndexOf(ModifiedMarker, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield break;
        }

        // Tables using the advanced VPW "dynamic ball shadows" system are not
        // running the simple ninuzzu routine; skip them.
        if (DynamicBallShadowsRegex().IsMatch(script))
        {
            yield break;
        }

        // The routine is often tweaked per-table, so match on any of several
        // stable signals: the header comment, the timer sub declaration, or the
        // characteristic shadow-position line (even if commented out).
        bool foundOriginal =
            BallShadowHeaderRegex().IsMatch(script) ||
            BallShadowTimerSubRegex().IsMatch(script) ||
            BallShadowPositionRegex().IsMatch(script);

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
