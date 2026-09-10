using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags an embedded "ball shadow" image whose Alpha Mask value is higher than
/// 1. A ball-shadow texture is meant to run with an Alpha Mask of -1, 0, or 1;
/// any larger value clips too much of the shadow and produces a hard, boxy edge
/// instead of the intended soft shadow.
///
/// This mirrors the PostItNote alpha-mask check but inverts the threshold: for
/// the note a high value is desirable, for the ball shadow a high value is the
/// problem. Like that rule, it matches by image name only (the "In Use" flag is
/// computed at runtime and not stored in the .vpx file). It's a VPX-specific
/// structural check, so it lives as a code rule rather than a JSON rule.
/// </summary>
public sealed class BallShadowAlphaMaskRule : ITableRule
{
    private const float MaxAlpha = 1f;

    public string Id => "ball-shadow-alpha-mask";

    public string Description =>
        "Ball shadow image should have an Alpha Mask of -1, 0, or 1 (not higher).";

    public bool EnabledByDefault => true;

    // Deep: reads the parsed image elements and their properties.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        foreach (TableElement element in context.Elements)
        {
            if (element is not VpxImage ||
                element.Name.IndexOf("ball", StringComparison.OrdinalIgnoreCase) < 0 ||
                element.Name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!element.Properties.TryGetValue(VpxImage.AlphaMaskKey, out object? value) ||
                value is not float alpha)
            {
                continue;
            }

            if (alpha > MaxAlpha)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Image '{element.Name}' has an Alpha Mask of {alpha:0.##}; " +
                    "ball shadow images should use -1, 0, or 1. A higher value clips " +
                    "the shadow and leaves a hard, boxy edge.")
                {
                    Element = element,
                };
            }
        }
    }
}
