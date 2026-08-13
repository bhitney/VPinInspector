using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Core.Reporting;

/// <summary>
/// The outcome of inspecting a single table: its findings (from table rules),
/// or a failure marker when the file could not be read. Carries enough identity
/// (path, game name) and state (flagged/clean/failed) to drive both the console
/// report and the interactive UI (rescan-flagged, clickable links).
/// </summary>
public sealed class TableReport
{
    public required string TableName { get; init; }

    public required string FilePath { get; init; }

    /// <summary>True when the file could not be read/parsed.</summary>
    public bool Failed { get; init; }

    /// <summary>Populated when <see cref="Failed"/> is true.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The resolved game/ROM name (VPX cGameName) when available, otherwise the
    /// table name. Empty when the table could not be read.
    /// </summary>
    public string GameName { get; init; } = string.Empty;

    /// <summary>The parsed table, when the read succeeded (null on failure).</summary>
    public PinballTable? Table { get; init; }

    /// <summary>Findings produced by table-scope rules for this table.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();

    /// <summary>True when the table read but at least one rule flagged it.</summary>
    public bool IsFlagged => !Failed && Findings.Count > 0;

    /// <summary>True when the table read successfully with no findings.</summary>
    public bool IsClean => !Failed && Findings.Count == 0;

    /// <summary>
    /// The table's overall severity: Error when it could not be read, otherwise
    /// the highest severity among its findings, or Info when clean. Every scanned
    /// table therefore always resolves to exactly one severity.
    /// </summary>
    public FindingSeverity Severity =>
        Failed
            ? FindingSeverity.Error
            : Findings.Count == 0
                ? FindingSeverity.Info
                : Findings.Max(f => f.Severity);
}

/// <summary>
/// The full result of a scan: an ordered set of per-table reports plus the
/// findings produced by collection-scope rules (PinUP match, VR-room, duplicate
/// cGameName, ...). Front-ends render this; they never touch platform internals.
/// </summary>
public sealed class ScanReport
{
    public required string InputPath { get; init; }

    public IReadOnlyList<TableReport> Tables { get; init; } = Array.Empty<TableReport>();

    /// <summary>
    /// Findings from collection rules, grouped by the rule that produced them,
    /// in registration order.
    /// </summary>
    public IReadOnlyList<CollectionFindingGroup> CollectionFindings { get; init; }
        = Array.Empty<CollectionFindingGroup>();
}

/// <summary>Collection-rule findings grouped under their rule for rendering.</summary>
public sealed class CollectionFindingGroup
{
    public required string RuleId { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
}
