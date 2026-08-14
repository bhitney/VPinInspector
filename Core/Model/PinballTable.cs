namespace VPin.Inspector.Core.Model;

/// <summary>
/// Platform-neutral base for a whole table document. A table CONTAINS elements
/// (composition) and adds table-wide facts. This is what rules receive.
///
/// A Future Pinball author subclasses this (FptTable) and never edits it.
/// </summary>
public abstract class PinballTable
{
    /// <summary>Absolute path to the source file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Display name for the table (usually the file name).</summary>
    public required string TableName { get; init; }

    private TableNameInfo? _nameInfo;

    /// <summary>
    /// The structured facts parsed from <see cref="TableName"/> (name,
    /// manufacturer, year, PUP flag). Parsed once on first access. Used by the
    /// well-formed-name rule and, later, online-database matching.
    /// </summary>
    public TableNameInfo NameInfo => _nameInfo ??= TableNameParser.Parse(TableName);

    /// <summary>
    /// Id of the platform that produced this table, e.g. "vpx" or "fpt".
    /// Must match the owning <see cref="IPinballPlatform.Id"/>.
    /// </summary>
    public abstract string PlatformId { get; }

    /// <summary>All elements on the table, platform-neutral.</summary>
    public abstract IReadOnlyList<TableElement> Elements { get; }

    /// <summary>
    /// The effective game/ROM name when the platform exposes one (VPX cGameName),
    /// otherwise null. Used by ROM-driven modules (DOF, VPS, ...).
    /// </summary>
    public virtual string? GameName { get; init; }

    /// <summary>
    /// The table's script source when the platform has one, otherwise null.
    /// Kept as raw text so script-based rules can parse it however they need.
    /// </summary>
    public virtual string? Script { get; init; }

    /// <summary>
    /// The table name embedded in the file's own metadata (VPX TableInfo), as
    /// authored, or null. Deliberately distinct from <see cref="TableName"/>
    /// (which is the file name) and <see cref="NameInfo"/> (parsed from the file
    /// name): embedded values are frequently stale or inaccurate, so they are
    /// named "Embedded*" and only populated by a deep parse. Later rules compare
    /// them against external sources (e.g. PinUP Popper) to flag mismatches.
    /// </summary>
    public virtual string? EmbeddedTableName { get; init; }

    /// <summary>
    /// The author embedded in the file's own metadata (VPX AuthorName), or null.
    /// Populated only by a deep parse. See <see cref="EmbeddedTableName"/>.
    /// </summary>
    public virtual string? EmbeddedAuthor { get; init; }

    /// <summary>
    /// The version embedded in the file's own metadata (VPX TableVersion), or
    /// null. Populated only by a deep parse. See <see cref="EmbeddedTableName"/>.
    /// </summary>
    public virtual string? EmbeddedFileVersion { get; init; }
}
