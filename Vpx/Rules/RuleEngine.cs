using System.Text.Json;

namespace VPin.Inspector.Vpx.Rules;

/// <summary>
/// Loads rules from JSON. Rule evaluation now lives in the modular pipeline
/// (DeclarativeElementRule + InspectionService); this type is retained purely as
/// the rules.json loader that exposes the parsed rules and settings.
/// </summary>
public sealed class RuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private RuleEngine(IReadOnlyList<InspectionRule> rules, InspectionSettings settings)
    {
        Rules = rules;
        Settings = settings;
    }

    /// <summary>The source rules loaded from the file, in file order.</summary>
    public IReadOnlyList<InspectionRule> Rules { get; }

    /// <summary>General scan settings loaded from the "settings" object.</summary>
    public InspectionSettings Settings { get; }

    /// <summary>Loads rules from a rules.json file on disk.</summary>
    public static RuleEngine LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>Loads rules from a JSON string.</summary>
    public static RuleEngine LoadFromJson(string json)
    {
        RuleSet? ruleSet = JsonSerializer.Deserialize<RuleSet>(json, JsonOptions);
        return new RuleEngine(
            ruleSet?.Rules ?? new List<InspectionRule>(),
            ruleSet?.Settings ?? new InspectionSettings());
    }
}
