using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Example VPX-specific rule: flags elements whose timer fires very frequently.
/// Declares SupportedPlatforms = {"vpx"} so it stays dormant for other
/// platforms. Demonstrates keying off the ITimerElement capability rather than
/// a concrete type.
/// </summary>
public sealed class FastTimerRule : ITableRule
{
    public string Id => "fast-timer";

    public string Description => "Flags timers with a very short interval (<10ms).";

    public bool EnabledByDefault => true;

    // Deep: inspects the table's elements/timers, requiring a full parse.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        foreach (TableElement element in context.Elements)
        {
            if (element is ITimerElement { HasTimer: true } timer &&
                timer.TimerIntervalMs < 10)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"'{element.Name}' ({element.TypeName}) has a {timer.TimerIntervalMs}ms timer.")
                {
                    Element = element,
                };
            }
        }
    }
}
