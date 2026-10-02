using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;
using VPin.Inspector.Vpx;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Flags embedded images with no detectable reference and reports their combined
/// reclaimable size. "Referenced" is decided in two layers:
/// <list type="bullet">
/// <item>Layer 0 (object tree): the image NAME appears in an element's image
/// slot (see <see cref="VpxGameItem.ImageReferencesKey"/>). This reproduces
/// VPX's "In Use" checkbox, which is computed at runtime and not stored.</item>
/// <item>Layer 1 (script): the image NAME is referenced in the table script,
/// matched conservatively so a script-driven image is never flagged
/// (see <see cref="ScriptAnalyzer.FindReferencedImageNames"/>).</item>
/// </list>
/// The <c>minSizeBytes</c> threshold suppresses tiny images (glyphs, color
/// swatches) so they aren't reported. When at least one candidate remains, an
/// Info summary lists the likely-unused image names and their total size, and a
/// Warning is emitted per candidate. Tables with no candidate produce nothing.
///
/// This rule is advisory only: an image with "no reference found" may still be
/// loaded dynamically (e.g. <c>EVAL</c> with names from an external source), so
/// findings are framed as "review before removing", never "delete".
/// </summary>
public sealed class ImageUsageRule : ITableRule
{
    private readonly ImageUsageSettings _settings;

    public ImageUsageRule(ImageUsageSettings settings)
    {
        _settings = settings;
    }

    public string Id => "image-usage";

    public string Description =>
        "Reports image space usage and flags images with no object-tree or script reference.";

    public bool EnabledByDefault => _settings.Enabled;

    // Deep: needs the parsed image elements, element image slots, and script.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        // Table-size gate: skip tables below the configured file-size floor so a
        // scan can focus on the largest tables first. 0 = evaluate everything.
        if (_settings.MinTableSizeMB > 0 && !IsAtLeastMb(context.Table.FilePath, _settings.MinTableSizeMB))
        {
            yield break;
        }

        var images = new List<VpxImage>();
        foreach (TableElement element in context.Elements)
        {
            if (element is VpxImage image)
            {
                images.Add(image);
            }
        }

        if (images.Count == 0)
        {
            yield break;
        }

        // Layer 0: union of every image name referenced by an element's slot.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TableElement element in context.Elements)
        {
            if (element.Properties.TryGetValue(VpxGameItem.ImageReferencesKey, out object? value) &&
                value is IReadOnlyList<string> slotNames)
            {
                foreach (string name in slotNames)
                {
                    referenced.Add(name);
                }
            }
        }

        // Layer 1: add any image whose name the script references.
        var imageNames = images
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        foreach (string scriptRef in ScriptAnalyzer.FindReferencedImageNames(context.Table.Script, imageNames))
        {
            referenced.Add(scriptRef);
        }

        // Candidates: images with no object-tree or script reference, above the
        // size threshold. The summary and the per-image warnings both derive
        // from this one set, so the summary only lists what might need fixing.
        var unused = images
            .Where(i => !string.IsNullOrEmpty(i.Name) && !referenced.Contains(i.Name))
            .Where(i => SizeOf(i) >= _settings.MinSizeBytes)
            .OrderByDescending(SizeOf)
            .ToList();

        if (unused.Count == 0)
        {
            yield break;
        }

        // Summary (Info): only the likely-unused images and their reclaimable size.
        long unusedBytes = unused.Sum(SizeOf);
        IEnumerable<VpxImage> listed = _settings.TopConsumers > 0
            ? unused.Take(_settings.TopConsumers)
            : unused;
        string names = string.Join(", ", listed.Select(i => i.Name));
        string more = _settings.TopConsumers > 0 && unused.Count > _settings.TopConsumers
            ? $", +{unused.Count - _settings.TopConsumers} more"
            : string.Empty;

        yield return new Finding(
            Id,
            FindingSeverity.Info,
            $"Likely unused images ({HumanBytes(unusedBytes)}): {names}{more}.");

        // Per-image findings (Warning) for the same candidates.
        foreach (VpxImage image in unused)
        {
            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                $"Image '{image.Name}' ({HumanBytes(SizeOf(image))}) has no object-tree or script " +
                "reference. It may be loaded dynamically; review before removing.")
            {
                Element = image,
            };
        }
    }

    private static long SizeOf(VpxImage image) =>
        image.Properties.TryGetValue(VpxImage.SizeBytesKey, out object? value) && value is int bytes
            ? bytes
            : 0L;

    /// <summary>
    /// True when the file at <paramref name="path"/> is at least
    /// <paramref name="minMb"/> megabytes. Returns true if the size can't be
    /// read, so a transient I/O issue never silently hides a table.
    /// </summary>
    private static bool IsAtLeastMb(string path, int minMb)
    {
        try
        {
            return new FileInfo(path).Length >= (long)minMb * 1024 * 1024;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>A byte count for people: <c>812 B</c>, <c>3.4 KB</c>, <c>12.0 MB</c>.</summary>
    private static string HumanBytes(long bytes)
    {
        if (bytes < 1000)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        string unit = "B";
        foreach (string next in new[] { "KB", "MB", "GB", "TB" })
        {
            value /= 1000.0;
            unit = next;
            if (value < 1000.0)
            {
                break;
            }
        }

        return $"{value:0.0} {unit}";
    }
}
