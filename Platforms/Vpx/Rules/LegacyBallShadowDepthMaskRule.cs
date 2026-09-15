using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags ball-shadow primitives that have NO "Hide parts behind" flag (BIFF
/// <c>ZMSK</c> / <c>m_useDepthMask</c>) stored at all. This happens on older
/// tables (e.g. pre-2018 exports) whose primitives predate the record being
/// written. When the record is absent VPX falls back to its built-in default
/// for the property, which is <c>true</c> (checked) — see
/// <c>DefaultPropsPrimitive DepthMask</c> in Visual Pinball. As a result the
/// shadow hides geometry behind it in the editor even though nothing is stored
/// in the file.
///
/// <para>
/// Unlike <see cref="BallShadowDepthMaskRule"/>, this rule is intentionally
/// <b>not</b> auto-fixable. The corrector performs fixed-width, in-place record
/// overwrites and cannot insert a brand-new <c>ZMSK</c> record without rewriting
/// the stream, so it is left out of
/// <see cref="VpxCorrectionWriter.FixableRuleIds"/>. The user must uncheck
/// "Hide parts behind" manually in VPX (which writes the record going forward).
/// </para>
/// </summary>
public sealed class LegacyBallShadowDepthMaskRule : ITableRule
{
    public string Id => "legacy-ball-shadow-depth-mask";

    public string Description =>
        "Legacy ball shadow primitives with no 'Hide parts behind' flag stored (VPX defaults it to checked).";

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

            // Only flag when the flag is entirely absent. When it is present the
            // auto-fixable BallShadowDepthMaskRule handles it instead.
            if (!element.Properties.ContainsKey(VpxGameItem.HidePartsBehindKey))
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Ball shadow primitive '{element.Name}' has no 'Hide parts behind' flag stored; " +
                    "VPX defaults it to checked (on), so the shadow hides geometry behind it. " +
                    "Open the table in VPX and uncheck 'Hide parts behind' on this primitive " +
                    "(this cannot be auto-fixed on legacy tables).")
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
