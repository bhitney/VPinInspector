using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Reporting;

/// <summary>
/// A single rendered line of report output tagged with a severity, so a UI can
/// color it and a console can flatten it to text. Standardizing on
/// <see cref="FindingSeverity"/> means every line resolves to exactly one of
/// Info / Warning / Error.
/// </summary>
public readonly record struct RenderedLine(FindingSeverity Severity, string Text)
{
    /// <summary>A neutral, non-colored line (headers, totals, blank lines).</summary>
    public static RenderedLine Info(string text) => new(FindingSeverity.Info, text);

    /// <summary>
    /// When true, this is a small vertical spacer between checklist entries. A UI
    /// can render it at a reduced font size for a fractional-height gap; a console
    /// treats it as an ordinary blank line.
    /// </summary>
    public bool IsSpacer { get; init; }

    /// <summary>A tiny inter-entry spacer (blank text, rendered short in the UI).</summary>
    public static RenderedLine Spacer() => new(FindingSeverity.Info, string.Empty) { IsSpacer = true };
}
