using System.Text.RegularExpressions;
using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Platforms.Vpx.Model;
using VPin.Inspector.Vpx.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// User-configurable variant of <see cref="BallShadowDepthMaskRule"/>. Instead
/// of the fixed built-in name match, it scans primitives against a list of
/// user-supplied regexes (from the "configurable-shadow" settings block in
/// rules.json) and flags any whose "Hide parts behind" (BIFF ZMSK) flag is
/// checked. Because a broad pattern (e.g. "shadow") can match many primitives,
/// it is disabled by default; enable it and tailor the patterns per collection
/// (for example "Divertershadow_(Left|Right)").
///
/// Findings from this rule are auto-fixable (ZMSK -> 0) just like the built-in
/// depth-mask rules.
/// </summary>
public sealed class ConfigurableShadowRule : ITableRule
{
    private readonly ConfigurableShadowSettings _settings;
    private readonly Regex[] _patterns;

    public ConfigurableShadowRule(ConfigurableShadowSettings settings)
    {
        _settings = settings;
        _patterns = BuildPatterns(settings.Patterns);
    }

    public string Id => "configurable-shadow";

    public string Description =>
        "Configurable shadow primitives (custom regex) should have 'Hide parts behind' unchecked.";

    // Opt-in: likely noisy, so off unless the user enables it in rules.json.
    public bool EnabledByDefault => _settings.Enabled;

    // Deep: reads the parsed primitive elements and their properties.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        if (_patterns.Length == 0)
        {
            yield break;
        }

        foreach (TableElement element in context.Elements)
        {
            if (element.TypeName != nameof(VpxItemType.Primitive) ||
                !MatchesAny(element.Name))
            {
                continue;
            }

            if (element.Properties.TryGetValue(VpxGameItem.HidePartsBehindKey, out object? value) &&
                value is true)
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Warning,
                    $"Shadow primitive '{element.Name}' has 'Hide parts behind' checked; " +
                    "it should be unchecked so the shadow doesn't hide geometry behind it.")
                {
                    Element = element,
                };
            }
        }
    }

    private bool MatchesAny(string name)
    {
        foreach (Regex pattern in _patterns)
        {
            if (pattern.IsMatch(name))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex[] BuildPatterns(IEnumerable<string> patterns)
    {
        var compiled = new List<Regex>();
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            try
            {
                compiled.Add(new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
            }
            catch (ArgumentException)
            {
                // Skip invalid user-supplied regexes rather than failing the scan.
            }
        }

        return compiled.ToArray();
    }
}
