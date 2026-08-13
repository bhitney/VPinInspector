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
