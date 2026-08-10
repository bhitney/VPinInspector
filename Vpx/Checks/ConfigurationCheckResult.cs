namespace VPX_Inspector.Vpx.Checks;

/// <summary>
/// The high-level family a check belongs to. Drives UI grouping and behavior:
/// Deep analysis runs per-table on any subset; Configuration runs once over the
/// whole collection (folder + external state) and requires a full scan.
/// </summary>
public enum CheckCategory
{
    DeepAnalysis,
    Configuration,
}

/// <summary>Severity of a rendered section within a check result.</summary>
public enum CheckSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A named group of finding lines within a check result, tagged with severity.
/// </summary>
public sealed class CheckSection
{
    public required string Title { get; init; }
    public required CheckSeverity Severity { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The outcome of running a configuration check. Generic so the report layer can
/// render any check uniformly.
/// </summary>
public sealed class ConfigurationCheckResult
{
    public required string CheckId { get; init; }
    public required string Title { get; init; }

    /// <summary>True when the check actually executed.</summary>
    public bool Ran { get; init; }

    /// <summary>Populated when the check was skipped or failed.</summary>
    public string? Skipped { get; init; }

    /// <summary>Header/context lines shown before the sections (e.g. counts).</summary>
    public IReadOnlyList<string> SummaryLines { get; init; } = Array.Empty<string>();

    /// <summary>Severity-tagged finding sections.</summary>
    public IReadOnlyList<CheckSection> Sections { get; init; } = Array.Empty<CheckSection>();

    public static ConfigurationCheckResult Skip(string checkId, string title, string reason) =>
        new() { CheckId = checkId, Title = title, Ran = false, Skipped = reason };
}
