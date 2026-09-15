using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags flipper-shadow primitives whose "Hide parts behind" flag (BIFF
/// <c>ZMSK</c> / <c>m_useDepthMask</c>) is checked. This mirrors
/// <see cref="BallShadowDepthMaskRule"/> but targets the per-flipper shadow
/// primitives, which are named with many abbreviated variations rather than the
/// full word "shadow" (which the ball-shadow rule keys off of).
///
/// Common naming conventions include "FlipperSh", "LFlipperSh"/"RFlipperSh",
/// "FlipperLSh"/"FlipperRSh", and "FlipperShadowL"/"FlipperShadowR" (L/R for the
/// left/right flipper). The shared trait is a name containing "flipper" together
/// with a shadow token ("shadow" or the "sh" abbreviation).
///
/// This is a VPX-specific structural check (it reads a primitive property that
/// the declarative rules.json schema can't express), so it lives as a code rule
/// rather than a JSON rule.
/// </summary>
public sealed class FlipperShadowDepthMaskRule : ITableRule
{
    public string Id => "flipper-shadow-depth-mask";

    public string Description =>
        "Flipper shadow primitives should have 'Hide parts behind' unchecked.";

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
                !IsFlipperShadowName(element.Name))
            {
                continue;
            }

            if (element.Properties.TryGetValue(VpxGameItem.HidePartsBehindKey, out object? value) &&
                value is true)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Flipper shadow primitive '{element.Name}' has 'Hide parts behind' checked; " +
                    "it should be unchecked so the shadow doesn't hide geometry behind it.")
                {
                    Element = element,
                };
            }
        }
    }

    /// <summary>
    /// True when the primitive name looks like a flipper shadow: it must contain
    /// "flipper" and a shadow token ("shadow", or the "sh" abbreviation).
    /// </summary>
    private static bool IsFlipperShadowName(string name)
    {
        if (name.IndexOf("flipper", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("sh", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
