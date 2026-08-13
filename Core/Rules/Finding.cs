using VPin.Inspector.Core.Model;

namespace VPin.Inspector.Core.Rules;

/// <summary>Severity of a single finding.</summary>
public enum FindingSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One neutral result produced by a rule. Front-ends (console, GUI, web) render
/// these however they like; rules never write to Console or a UI directly.
/// </summary>
public sealed record Finding(
    string RuleId,
    FindingSeverity Severity,
    string Message)
{
    /// <summary>Optional element this finding relates to.</summary>
    public TableElement? Element { get; init; }

    /// <summary>Optional extra key/value detail for rich rendering.</summary>
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}
