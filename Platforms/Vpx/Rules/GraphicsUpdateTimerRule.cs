using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Some tables use a dedicated "graphics" timer element to coordinate per-frame
/// visual updates (ball/flipper shadows, primitive animations, etc.). Common
/// names include <c>GraphicsUpdate</c>, <c>GraphicsTimer</c>,
/// <c>GraphicsUpdateTimer</c>, or simply <c>Graphics</c>. This timer is meant to
/// fire once per rendered frame, which VPX expresses as a timer interval of
/// <c>-1</c> ("update as fast as the display refreshes").
///
/// A very common misconfiguration is leaving the interval at a fixed value such
/// as <c>10</c> ms, which decouples the updates from the frame rate and causes
/// stutter or extra CPU load. This rule looks for a <see cref="ITimerElement"/>
/// whose name starts with "Graphics" (spaces/casing ignored) and, if present,
/// verifies its interval is <c>-1</c>; otherwise it flags the table.
/// </summary>
public sealed class GraphicsUpdateTimerRule : ITableRule
{
    public string Id => "graphics-update-timer";

    public string Description =>
        "Flags a 'Graphics' timer whose interval is not -1 (per-frame).";

    public bool EnabledByDefault => true;

    // Deep: inspects the table's elements/timers, requiring a full parse.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        foreach (TableElement element in context.Elements)
        {
            if (element is not ITimerElement timer)
            {
                continue;
            }

            // Only consider elements that are actually timers (a Timer element,
            // or any element with its timer turned on) whose name starts with
            // "Graphics".
            bool isTimerElement =
                timer.TimerEnabled ||
                string.Equals(element.TypeName, "Timer", StringComparison.OrdinalIgnoreCase);

            if (!isTimerElement || !IsGraphicsName(element.Name))
            {
                continue;
            }

            if (timer.TimerIntervalMs != -1)
            {
                string state = timer.TimerEnabled ? "[ENABLED]" : "[DISABLED]";
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"'{element.Name}' ({element.TypeName}) {state} Graphics timer is " +
                    $"set to {timer.TimerIntervalMs}ms; it should be -1 (per-frame).")
                {
                    Element = element,
                };
            }
        }
    }

    // Matches any name that starts with "Graphics" ignoring case and any
    // internal whitespace, so "GraphicsUpdate", "GraphicsTimer",
    // "GraphicsUpdateTimer", "Graphics Update", and plain "Graphics" all match.
    private static bool IsGraphicsName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;
        foreach (char c in name)
        {
            if (!char.IsWhiteSpace(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return buffer[..length].StartsWith("graphics");
    }
}
