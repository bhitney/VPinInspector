using System.Text.RegularExpressions;
using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Rules; // reuse InspectionRule + IntervalCondition from rules.json

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Bridges a single user-authored rule from rules.json (<see cref="InspectionRule"/>)
/// into the new pipeline as an <see cref="ITableRule"/>. Name/type/interval
/// matching is evaluated against the neutral <see cref="TableElement"/> model
/// (using the <see cref="ITimerElement"/> capability for timers), so the same
/// JSON authoring keeps working while flowing through InspectionService.
/// </summary>
public sealed class DeclarativeElementRule : ITableRule
{
    private readonly InspectionRule _source;
    private readonly Regex? _nameRegex;
    private readonly HashSet<string>? _types;
    private readonly IntervalCondition? _condition;
    private readonly FindingSeverity _severity;

    public DeclarativeElementRule(InspectionRule source)
    {
        _source = source;
        _severity = ParseSeverity(source.Severity);

        if (source.NamePatterns.Count > 0)
        {
            string combined = string.Join(
                "|",
                source.NamePatterns
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(GlobToRegex));

            if (combined.Length > 0)
            {
                _nameRegex = new Regex(
                    combined,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
        }

        if (source.Types.Count > 0)
        {
            var types = source.Types
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _types = types.Count > 0 ? types : null;
        }

        _condition = IntervalCondition.TryParse(source.Interval);
    }

    public string Id => _source.Id;

    public string Description => _source.Description;

    public bool EnabledByDefault => _source.Enabled;

    // rules.json rules are authored against VPX today.
    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(TableContext context)
    {
        foreach (TableElement element in context.Elements)
        {
            if (Matches(element))
            {
                yield return new Finding(
                    Id,
                    _severity,
                    string.IsNullOrEmpty(_source.Description)
                        ? $"'{element.Name}' ({element.TypeName}) matched rule '{Id}'."
                        : _source.Description)
                {
                    Element = element,
                };
            }
        }
    }

    private bool Matches(TableElement element)
    {
        var timer = element as ITimerElement;

        if (_source.MustBeTimer && timer is not { HasTimer: true })
        {
            return false;
        }

        if (_types is not null && !_types.Contains(element.TypeName))
        {
            return false;
        }

        if (_nameRegex is not null && !_nameRegex.IsMatch(element.Name))
        {
            return false;
        }

        if (_condition is not null)
        {
            if (timer is not { HasTimer: true })
            {
                return false;
            }

            if (!_condition.IsSatisfiedBy(timer.TimerIntervalMs))
            {
                return false;
            }
        }

        return true;
    }

    private static string GlobToRegex(string glob)
    {
        string escaped = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^(?:{escaped})$";
    }

    /// <summary>
    /// Maps a rules.json severity string to a <see cref="FindingSeverity"/>,
    /// defaulting to Warning when null/empty/unrecognized.
    /// </summary>
    private static FindingSeverity ParseSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "info" => FindingSeverity.Info,
            "error" => FindingSeverity.Error,
            "warning" => FindingSeverity.Warning,
            _ => FindingSeverity.Warning,
        };
}
