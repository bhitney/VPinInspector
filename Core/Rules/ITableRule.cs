using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Platforms;

namespace VPin.Inspector.Core.Rules;

/// <summary>
/// Everything a table-scope rule needs for a single table. The core builds one
/// per table and caches the parsed <see cref="Table"/>, so multiple rules that
/// all scan elements or the script share the same parse work.
/// </summary>
public sealed class TableContext
{
    public required IPinballPlatform Platform { get; init; }
    public required PinballTable Table { get; init; }

    /// <summary>Convenience passthrough to the table's elements.</summary>
    public IReadOnlyList<TableElement> Elements => Table.Elements;
}

/// <summary>
/// How expensive a rule is to evaluate, which controls how much of each table
/// the core must parse before running it.
/// <list type="bullet">
/// <item><see cref="Quick"/> — needs only cheap facts (file names, folder
/// listings, external databases). No table body parse required.</item>
/// <item><see cref="Deep"/> — must fully parse each table (its elements and/or
/// script, e.g. cGameName), which is slow.</item>
/// </list>
/// This axis is independent of rule scope: a collection rule can be Deep
/// (duplicate-game-name reads cGameName) and a table rule can be Quick
/// (well-formed-name only reads the file name).
/// </summary>
public enum AnalysisDepth
{
    Quick,
    Deep,
}

/// <summary>
/// Common metadata shared by every rule/module. A rule is enabled or disabled
/// independently, so nobody is forced to run DOF, PinUP, VPS, etc.
/// </summary>
public interface IInspectionRule
{
    /// <summary>Stable id, e.g. "ball-shadow", "dof-lookup", "vps-version".</summary>
    string Id { get; }

    /// <summary>Human-readable description for UI and reports.</summary>
    string Description { get; }

    /// <summary>
    /// How much table parsing this rule requires. Quick rules can run without a
    /// full table parse; a scan that selects only Quick rules skips the
    /// expensive load entirely.
    /// </summary>
    AnalysisDepth Depth { get; }

    /// <summary>
    /// Whether this rule runs by default. Combined with per-run selection so a
    /// user can opt in/out of any module without touching others.
    /// </summary>
    bool EnabledByDefault { get; }

    /// <summary>
    /// Platform ids this rule supports. Empty set = platform-agnostic (runs for
    /// every platform). The runner skips a rule whose set excludes the current
    /// table's platform, so VPX-only rules stay dormant for Future Pinball.
    /// </summary>
    IReadOnlySet<string> SupportedPlatforms { get; }
}

/// <summary>A rule that inspects a single table.</summary>
public interface ITableRule : IInspectionRule
{
    IEnumerable<Finding> Evaluate(TableContext context);
}
