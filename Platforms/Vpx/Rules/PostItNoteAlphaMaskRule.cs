using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags a "PostItNote" image whose Alpha Mask value is very low (&lt;= 1, and
/// it can be negative). The note is still visible at a low alpha mask, but the
/// edges show artifacts unless the value is raised to clip them; a value around
/// 50 usually does it.
///
/// Note: the editor's "In Use" checkbox is computed at runtime and isn't stored
/// in the .vpx file, so it can't be used to narrow the scan. The rule therefore
/// matches by image name only.
/// </summary>
public sealed class PostItNoteAlphaMaskRule : ITableRule
{
    private const string ImageName = "PostItNote";
    private const float RecommendedAlpha = 50f;

    public string Id => "postitnote-alpha-mask";

    public string Description =>
        "PostItNote image should have an Alpha Mask of ~50 or higher (not <= 1).";

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
                !string.Equals(element.Name, ImageName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!element.Properties.TryGetValue(VpxImage.AlphaMaskKey, out object? value) ||
                value is not float alpha)
            {
                continue;
            }

            if (alpha <= 1f)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Image '{element.Name}' has an Alpha Mask of {alpha:0.##}; " +
                    $"values <= 1 leave edge artifacts on the note. Raise it to " +
                    $"about {RecommendedAlpha:0} to clip the edges.")
                {
                    Element = element,
                };
            }
        }
    }
}
