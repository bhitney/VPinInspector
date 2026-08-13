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
}
