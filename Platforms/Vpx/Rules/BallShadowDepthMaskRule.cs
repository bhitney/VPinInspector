using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags ball-shadow primitives whose "Hide parts behind" flag (BIFF
/// <c>ZMSK</c> / <c>m_useDepthMask</c>) is checked. Many tables render the ball
/// shadow with one primitive per ball (BallShadow1, BallShadow2, ..., or just
/// Shadow1, Shadow2, ...). For these to draw correctly the depth mask must be
/// unchecked; when it is checked the shadow hides geometry behind it.
///
/// This is a VPX-specific structural check (it reads a primitive property that
/// the declarative rules.json schema can't express), so it lives as a code rule
/// rather than a JSON rule.
/// </summary>
public sealed class BallShadowDepthMaskRule : ITableRule
{
    public string Id => "ball-shadow-depth-mask";

    public string Description =>
        "Ball shadow primitives should have 'Hide parts behind' unchecked.";

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
                !IsBallShadowName(element.Name))
            {
                continue;
            }

            if (element.Properties.TryGetValue(VpxGameItem.HidePartsBehindKey, out object? value) &&
                value is true)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Ball shadow primitive '{element.Name}' has 'Hide parts behind' checked; " +
                    "it should be unchecked so the shadow doesn't hide geometry behind it.")
                {
                    Element = element,
                };
            }
        }
    }

    // Requires BOTH "ball" and "shadow" in the name so it matches BallShadow1,
    // RtxBallShadow1, etc., but not unrelated primitives like "VR_Clock Shadow".
    private static bool IsBallShadowName(string name) =>
        name.IndexOf("ball", StringComparison.OrdinalIgnoreCase) >= 0 &&
        name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;
}
