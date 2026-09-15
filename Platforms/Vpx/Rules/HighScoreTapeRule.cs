using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags "high score tape" primitives that are misconfigured. These primitives
/// are the translucent tape strips overlaid on the playfield/apron for
/// high-score initials; they are conventionally named <c>PTape</c>,
/// <c>PTape1</c>, etc. For them to render correctly they must have:
/// <list type="bullet">
/// <item>"Hide parts behind" (BIFF <c>ZMSK</c>) unchecked,</item>
/// <item>"Static Rendering" (BIFF <c>STRE</c>) unchecked,</item>
/// <item>"Reflection Enabled" (BIFF <c>REEN</c>) unchecked,</item>
/// <item>the physics behavior set to "Toy" / never collidable (BIFF
/// <c>ISTO</c> = true) rather than Collidable.</item>
/// </list>
/// Any one of these being wrong triggers the rule, and the auto-fixer corrects
/// all of them.
///
/// This is a VPX-specific structural check (it reads primitive properties that
/// the declarative rules.json schema can't express), so it lives as a code rule
/// rather than a JSON rule.
/// </summary>
public sealed class HighScoreTapeRule : ITableRule
{
    public string Id => "high-score-tape";

    public string Description =>
        "High score tape primitives should have 'Hide parts behind', 'Static Rendering', " +
        "and 'Reflection Enabled' unchecked, and be set to 'Toy' (never collidable).";

    public bool EnabledByDefault => true;

    // Deep: reads the parsed primitive elements and their properties.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        foreach (TableElement element in context.Elements)
        {
            if (element.TypeName != nameof(VpxItemType.Primitive) ||
                !IsTapeName(element.Name))
            {
                continue;
            }

            var problems = new List<string>();

            if (IsFlagSet(element, VpxGameItem.HidePartsBehindKey))
            {
                problems.Add("'Hide parts behind' is checked");
            }

            if (IsFlagSet(element, VpxGameItem.StaticRenderingKey))
            {
                problems.Add("'Static Rendering' is checked");
            }

            if (IsFlagSet(element, VpxGameItem.ReflectionEnabledKey))
            {
                problems.Add("'Reflection Enabled' is checked");
            }

            // Should be a Toy (never collidable). If the flag is present and
            // false, it is Collidable and needs correcting.
            if (element.Properties.TryGetValue(VpxGameItem.ToyKey, out object? toy) &&
                toy is false)
            {
                problems.Add("physics is 'Collidable' (should be 'Toy')");
            }

            if (problems.Count > 0)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"High score tape primitive '{element.Name}' is misconfigured: " +
                    string.Join(", ", problems) + ".")
                {
                    Element = element,
                };
            }
        }
    }

    // True when the named bool property is present and set to true.
    private static bool IsFlagSet(TableElement element, string key) =>
        element.Properties.TryGetValue(key, out object? value) && value is true;

    // Matches "PTape" or any name starting with "PTape" (PTape1, PTape2, ...),
    // case-insensitively.
    private static bool IsTapeName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.StartsWith("PTape", StringComparison.OrdinalIgnoreCase);
}
