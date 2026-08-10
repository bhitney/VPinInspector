using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPX_Inspector.Vpx.Rules;

/// <summary>
/// A single element that matched a rule.
/// </summary>
public sealed record RuleMatch(InspectionRule Rule, GameItem Item);

/// <summary>
/// Loads rules from JSON and evaluates them against a set of parsed elements.
/// </summary>
public sealed class RuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IReadOnlyList<CompiledRule> _rules;

    private RuleEngine(IReadOnlyList<CompiledRule> rules, InspectionSettings settings)
    {
        _rules = rules;
        Settings = settings;
    }

    /// <summary>The source rules loaded into this engine, in file order.</summary>
    public IReadOnlyList<InspectionRule> Rules => _rules.Select(r => r.Source).ToList();

    /// <summary>General scan settings loaded from the "settings" object.</summary>
    public InspectionSettings Settings { get; }

    /// <summary>Loads and compiles rules from a rules.json file on disk.</summary>
    public static RuleEngine LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>Loads and compiles rules from a JSON string.</summary>
    public static RuleEngine LoadFromJson(string json)
    {
        RuleSet? ruleSet = JsonSerializer.Deserialize<RuleSet>(json, JsonOptions);
        var compiled = (ruleSet?.Rules ?? new List<InspectionRule>())
            .Select(CompiledRule.Compile)
            .ToList();
        return new RuleEngine(compiled, ruleSet?.Settings ?? new InspectionSettings());
    }

    /// <summary>
    /// Evaluates every enabled rule against every element and returns all matches.
    /// </summary>
    /// <param name="items">The parsed elements to test.</param>
    /// <param name="enabledRuleIds">
    /// When provided, only rules whose id is in this set are evaluated (a per-run
    /// override, e.g. from UI checkboxes). When null, each rule's own
    /// <see cref="InspectionRule.Enabled"/> flag is honored instead.
    /// </param>
    public IReadOnlyList<RuleMatch> Evaluate(
        IEnumerable<GameItem> items,
        IReadOnlySet<string>? enabledRuleIds = null)
    {
        var matches = new List<RuleMatch>();
        var itemList = items as IReadOnlyList<GameItem> ?? items.ToList();

        foreach (CompiledRule rule in _rules)
        {
            bool isEnabled = enabledRuleIds is not null
                ? enabledRuleIds.Contains(rule.Source.Id)
                : rule.Source.Enabled;

            if (!isEnabled)
            {
                continue;
            }

            foreach (GameItem item in itemList)
            {
                if (rule.Matches(item))
                {
                    matches.Add(new RuleMatch(rule.Source, item));
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// A rule with its name patterns pre-compiled to a single regex and its
    /// interval condition parsed once.
    /// </summary>
    private sealed class CompiledRule
    {
        public required InspectionRule Source { get; init; }
        private Regex? NameRegex { get; init; }
        private IntervalCondition? Condition { get; init; }
        private HashSet<string>? Types { get; init; }

        public static CompiledRule Compile(InspectionRule rule)
        {
            Regex? nameRegex = null;
            if (rule.NamePatterns.Count > 0)
            {
                string combined = string.Join(
                    "|",
                    rule.NamePatterns
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(GlobToRegex));

                if (combined.Length > 0)
                {
                    nameRegex = new Regex(
                        combined,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                }
            }

            HashSet<string>? types = null;
            if (rule.Types.Count > 0)
            {
                types = rule.Types
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (types.Count == 0)
                {
                    types = null;
                }
            }

            return new CompiledRule
            {
                Source = rule,
                NameRegex = nameRegex,
                Condition = IntervalCondition.TryParse(rule.Interval),
                Types = types,
            };
        }

        public bool Matches(GameItem item)
        {
            if (Source.MustBeTimer && !item.HasTimer)
            {
                return false;
            }

            if (Types is not null && !Types.Contains(item.TypeName))
            {
                return false;
            }

            if (NameRegex is not null && !NameRegex.IsMatch(item.Name))
            {
                return false;
            }

            if (Condition is not null)
            {
                // An interval condition only makes sense for elements with a timer.
                if (!item.HasTimer)
                {
                    return false;
                }

                if (!Condition.IsSatisfiedBy(item.TimerIntervalMs))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Converts a glob (with '*' and '?') into an anchored regex fragment.
        /// </summary>
        private static string GlobToRegex(string glob)
        {
            var escaped = Regex.Escape(glob)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return $"^(?:{escaped})$";
        }
    }
}
