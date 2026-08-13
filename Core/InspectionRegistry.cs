using VPin.Inspector.Core.Platforms;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Core;

/// <summary>
/// Central registry of pluggable platforms and rules. This is the ONE place a
/// new module (a Future Pinball platform, a DOF rule, a VPS version checker) is
/// wired in. Nothing here is required: every rule is opt-in, so a user who
/// doesn't use DOF/PinUP/VPS simply doesn't enable those rules.
///
/// Kept as explicit registration for now (simple, no reflection). Can be
/// upgraded to assembly scanning later without changing the contracts.
/// </summary>
public sealed class InspectionRegistry
{
    private readonly List<IPinballPlatform> _platforms = new();
    private readonly List<ITableRule> _tableRules = new();
    private readonly List<ICollectionRule> _collectionRules = new();

    public IReadOnlyList<IPinballPlatform> Platforms => _platforms;
    public IReadOnlyList<ITableRule> TableRules => _tableRules;
    public IReadOnlyList<ICollectionRule> CollectionRules => _collectionRules;

    public InspectionRegistry AddPlatform(IPinballPlatform platform)
    {
        _platforms.Add(platform);
        return this;
    }

    public InspectionRegistry AddTableRule(ITableRule rule)
    {
        _tableRules.Add(rule);
        return this;
    }

    public InspectionRegistry AddCollectionRule(ICollectionRule rule)
    {
        _collectionRules.Add(rule);
        return this;
    }

    /// <summary>All file extensions across registered platforms, for discovery.</summary>
    public IReadOnlyList<string> AllFileExtensions =>
        _platforms.SelectMany(p => p.FileExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Finds the first platform that can open the given file, or null.</summary>
    public IPinballPlatform? ResolvePlatform(string filePath) =>
        _platforms.FirstOrDefault(p => p.CanHandle(filePath));
}
