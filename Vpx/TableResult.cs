using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx;

/// <summary>
/// The outcome of scanning a single .vpx table: its rule matches, or a failure
/// marker when the file could not be read as a VPX compound file.
/// </summary>
public sealed record TableResult(
    string TableName,
    string FilePath,
    bool Failed,
    IReadOnlyList<RuleMatch> Matches)
{
    /// <summary>True when at least one rule matched.</summary>
    public bool IsFlagged => !Failed && Matches.Count > 0;

    /// <summary>True when the table read successfully with no matches.</summary>
    public bool IsClean => !Failed && Matches.Count == 0;

    /// <summary>Optional error message captured when <see cref="Failed"/> is true.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The resolved game name for the table: the script's cGameName when present,
    /// otherwise the table name. Empty when the table could not be read.
    /// </summary>
    public string GameName { get; init; } = string.Empty;
}
