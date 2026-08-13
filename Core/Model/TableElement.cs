namespace VPin.Inspector.Core.Model;

/// <summary>
/// Platform-neutral base for a single element on a table (a flipper, a bumper,
/// a light, ...). Kept deliberately THIN: it holds only what every emulator can
/// reasonably be expected to have. Anything platform- or category-specific is
/// exposed either through a capability interface (see <see cref="ITimerElement"/>)
/// or the untyped <see cref="Properties"/> bag.
///
/// A Future Pinball author subclasses this (e.g. FptElement) and never edits it.
/// </summary>
public abstract class TableElement
{
    /// <summary>
    /// Stable per-table identifier for the element (VPX uses the stream name,
    /// another platform might use a GUID or index). Unique within one table.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-facing element name as authored in the editor.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Platform's own type label for the element, e.g. "Flipper", "Bumper".
    /// Each platform decides how to derive this.
    /// </summary>
    public abstract string TypeName { get; }

    /// <summary>
    /// Escape hatch for platform- or element-specific fields that don't warrant
    /// a dedicated capability interface. Lets a rule author reach an obscure
    /// property (e.g. Properties["Elasticity"]) without a bespoke subclass.
    /// Rules should treat missing keys gracefully.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; }
        = new Dictionary<string, object?>();
}
