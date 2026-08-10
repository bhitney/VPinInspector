using System.Text.Json.Serialization;

namespace VPX_Inspector.Vpx.Rules;

/// <summary>
/// The root object deserialized from rules.json.
/// </summary>
public sealed class RuleSet
{
    [JsonPropertyName("settings")]
    public InspectionSettings Settings { get; init; } = new();

    [JsonPropertyName("rules")]
    public List<InspectionRule> Rules { get; init; } = new();
}

/// <summary>
/// General (non-rule) scan settings read from the "settings" object in rules.json.
/// </summary>
public sealed class InspectionSettings
{
    /// <summary>
    /// Optional maximum wall-clock scan time in seconds. When greater than zero,
    /// scanning stops once the elapsed time exceeds this budget (useful for
    /// testing against a subset without scanning everything). Zero/absent = no limit.
    /// </summary>
    [JsonPropertyName("maxRunTimeSeconds")]
    public double MaxRunTimeSeconds { get; init; }

    /// <summary>
    /// Optional file-name globs to exclude from scanning, e.g. [ "VR ROOM*" ].
    /// Supports '*' and '?' wildcards, matched case-insensitively against the
    /// table's file name (without directory).
    /// </summary>
    [JsonPropertyName("excludePatterns")]
    public List<string> ExcludePatterns { get; init; } = new();
}

/// <summary>
/// A single user-defined rule describing which elements to match and what
/// interval condition flags a problem.
/// </summary>
public sealed class InspectionRule
{
    /// <summary>Stable identifier, e.g. "ball-shadow".</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable explanation shown when the rule matches.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the rule participates in scans. Defaults to true. Can be toggled
    /// per-run from the UI without persisting back to the file.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Name variants to match. Supports '*' and '?' wildcards, matched
    /// case-insensitively (e.g. "BallShadow", "*sling*").
    /// </summary>
    [JsonPropertyName("namePatterns")]
    public List<string> NamePatterns { get; init; } = new();

    /// <summary>When true, only elements that carry a timer are considered.</summary>
    [JsonPropertyName("mustBeTimer")]
    public bool MustBeTimer { get; init; }

    /// <summary>
    /// Optional element-type filter, e.g. [ "Timer" ] or [ "Timer", "Light" ].
    /// Matched case-insensitively against the element's type name. When empty,
    /// elements of any type are considered.
    /// </summary>
    [JsonPropertyName("types")]
    public List<string> Types { get; init; } = new();

    /// <summary>
    /// Interval condition applied to the timer interval in milliseconds,
    /// expressed as an operator + number, e.g. "&gt;=10", "&lt;10", "&gt;40".
    /// When omitted, any matching element is reported.
    /// </summary>
    [JsonPropertyName("interval")]
    public string? Interval { get; init; }

    /// <summary>
    /// Optional recommended timer interval in milliseconds for elements that
    /// match this rule. Purely advisory for now — reported as a proposed change,
    /// never written back to the table.
    /// </summary>
    [JsonPropertyName("suggest")]
    public int? Suggest { get; init; }
}
