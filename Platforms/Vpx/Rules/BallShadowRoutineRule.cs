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
/// whose Ballsize/width divisors (<c>/6</c> and <c>/7</c>, or tweaked variants
/// such as <c>/16</c> and <c>/17</c>) and trailing offset (<c>+ 6</c>,
/// <c>+ 10</c>, <c>+ 2</c>, or commented out) vary between tables.
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
    // the variable trailing offset. Anchored per-line and requires the line to
    // NOT be commented before the assignment: a commented-out copy (often left
    // behind when the author replaces it with a similar line) is not a reliable
    // signal that the routine is active, so it's ignored. The Ballsize and
    // Table1.Width divisors vary between tables (e.g. the canonical "/6 ... /7"
    // versus tweaked "/16 ... /17" variants), so they are matched as generic
    // integers rather than the specific canonical values.
    [GeneratedRegex(
        @"^(?:(?!').)*?BallShadow\(b\)\.X\s*=.*BOT\(b\)\.X.*Ballsize\s*/\s*\d+.*Table1\.Width\s*/\s*2.*/\s*\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BallShadowPositionRegex();

    // Matches a shadow-position line whose active (uncommented) expression is
    // immediately followed by a commented-out signed offset, e.g.
    // <code>BallShadow(b).X = (...)' + 13</code>. The
    // <c>(?:(?!').)*?</c> segments guarantee the apostrophe we match is the
    // FIRST one on the line, so the whole expression up to it runs live and
    // only the offset is commented out. Tables typically have two such lines
    // (one per If/Else branch, "+ N" and "- N"); when the differentiating
    // offset is commented out on both, the branches compute identical values,
    // making the surrounding If/Else pointless and mispositioning the shadow.
    [GeneratedRegex(
        @"^(?:(?!').)*?BallShadow\(b\)\.X\s*=(?:(?!').)*?Table1\.Width(?:(?!').)*?'\s*[-+]\s*\d+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BallShadowCommentedOffsetRegex();

    // Matches the VPW "dynamic ball shadows" constant. Its presence (even when
    // commented out or set to 0) indicates the more advanced shadow system
    // rather than the simple ninuzzu routine, so the table is not flagged.
    [GeneratedRegex(
        @"DynamicBallShadowsOn",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DynamicBallShadowsRegex();

    // Matches a "ZSHA" comment header used by tables that already ship a modern
    // ambient ball-shadow implementation. Only the "' ZSHA" prefix is required,
    // since the label text varies between tables. Whitespace is ignored between
    // the comment marker and "ZSHA". When present alongside a BSInit sub, the
    // table is considered good and not flagged.
    [GeneratedRegex(
        @"'\s*ZSHA",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZshaAmbientRegex();

    // Matches the "Sub BSInit()" declaration that accompanies the ZSHA ambient
    // ball-shadow implementation.
    [GeneratedRegex(
        @"\bSub\s+BSInit\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BsInitSubRegex();

    // Matches the "JP's VP10 Rolling Sounds + Ballshadow" comment header, which
    // identifies a known-good ball-shadow implementation. Whitespace is ignored
    // between tokens.
    [GeneratedRegex(
        @"'\s*JP's\s+VP10\s+Rolling\s+Sounds\s*\+\s*Ballshadow",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JpRollingSoundsRegex();

    // Matches a "ZSHD" comment header used by tables that already ship a modern
    // ball-shadow implementation. Only the "' ZSHD" prefix is required; the
    // asterisks/spacing between the comment marker and "ZSHD" are optional.
    [GeneratedRegex(
        @"^\s*'[\s*]*ZSHD",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ZshdRegex();

    // Matches a comment line that starts with "JP's VP10" (whitespace optional),
    // identifying a known-good ball-shadow implementation.
    [GeneratedRegex(
        @"^\s*'\s*JP's\s+VP10",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex JpVp10LineRegex();

    // Matches the "marker - bthLonewolf" tag (e.g. "' altshadow marker -
    // bthLonewolf") that authors add to indicate a known-good ball-shadow
    // implementation. Whitespace around the dash is ignored.
    [GeneratedRegex(
        @"marker\s*-\s*bthLonewolf",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerBthLonewolfRegex();

    // Matches a GlowBall signal: either an "IF GlowBall..." statement or a
    // "Glow Ball code" comment. Tables using glowing balls typically don't use
    // ball shadows, so their presence indicates a likely false positive.
    [GeneratedRegex(
        @"\bIf\s+GlowBall|Glow\s*Ball\s+code",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GlowBallRegex();

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

        // Tables that ship the modern ZSHA ambient ball-shadow implementation
        // (header comment + BSInit sub) are considered good; skip them.
        if (ZshaAmbientRegex().IsMatch(script) && BsInitSubRegex().IsMatch(script))
        {
            yield break;
        }

        // Tables using JP's VP10 Rolling Sounds + Ballshadow implementation are
        // considered good; skip them.
        if (JpRollingSoundsRegex().IsMatch(script))
        {
            yield break;
        }

        // Tables carrying a "' ZSHD" comment header (asterisks/spacing optional)
        // ship a modern ball-shadow implementation; skip them.
        if (ZshdRegex().IsMatch(script))
        {
            yield break;
        }

        // Tables with a comment line starting with "JP's VP10" (whitespace
        // optional) are considered good; skip them.
        if (JpVp10LineRegex().IsMatch(script))
        {
            yield break;
        }

        // Tables carrying a "marker - bthLonewolf" tag have a known-good
        // ball-shadow implementation; skip them.
        if (MarkerBthLonewolfRegex().IsMatch(script))
        {
            yield break;
        }

        // Tables using glowing balls (an "IF GlowBall..." statement or a
        // "Glow Ball code" comment) typically don't use ball shadows, so this is
        // a likely false positive; skip them.
        if (GlowBallRegex().IsMatch(script))
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

        // Detect the "both branches identical" design flaw: the If/Else picks
        // between two shadow-position lines that should differ only by a signed
        // offset (e.g. "+ 13" vs "- 13"), but the offset has been commented out
        // on both. With the offsets gone both branches compute the same value,
        // making the If/Else pointless and mispositioning the shadow. Requires
        // at least two such lines so a single commented-out leftover copy
        // doesn't trigger a false positive.
        int commentedOffsetLines = BallShadowCommentedOffsetRegex().Matches(script).Count;
        if (commentedOffsetLines >= 2)
        {
            yield return new Finding(
                "ball-shadow-commented-offset",
                FindingSeverity.Warning,
                "Ball shadow routine has its horizontal offset commented out on " +
                "both If/Else branches, so both branches compute the same X " +
                "position; the branch is pointless and the shadow is " +
                "mispositioned. Review the shadow offset design.");
        }
    }
}
