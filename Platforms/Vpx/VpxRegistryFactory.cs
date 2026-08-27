using VPin.Inspector.Core;
using VPin.Inspector.Platforms.Vpx;
using VPin.Inspector.Platforms.Vpx.Rules;
using VPin.Inspector.Vpx.Rules; // RuleEngine loads rules.json

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// Composition root for the VPX platform: builds an <see cref="InspectionRegistry"/>
/// wired with the VPX platform, the user's rules.json (as declarative rules), and
/// the opt-in integrations. This is the single place new modules are registered;
/// a Future Pinball host would add its platform/rules here (or in its own root)
/// without touching Core.
/// </summary>
public static class VpxRegistryFactory
{
    public static InspectionRegistry Build(string rulesJsonPath)
    {
        RuleEngine engine = RuleEngine.LoadFromFile(rulesJsonPath);
        return Build(engine);
    }

    /// <summary>
    /// Builds the registry from an already-loaded rule engine (so a caller that
    /// also needs the settings can share one instance).
    /// </summary>
    public static InspectionRegistry Build(RuleEngine engine) => Build(engine, engine.Settings);

    /// <summary>
    /// Builds the registry from an engine's rules but with an override settings
    /// object (e.g. edits made in the UI settings panel for this run).
    /// </summary>
    public static InspectionRegistry Build(RuleEngine engine, InspectionSettings settings)
    {
        var registry = new InspectionRegistry();

        // 1. Platforms.
        registry.AddPlatform(new VpxPlatform());

        // 2. User-authored rules.json -> declarative table rules.
        foreach (InspectionRule rule in engine.Rules)
        {
            registry.AddTableRule(new DeclarativeElementRule(rule));
        }

        // 3. Opt-in integrations (each reports its own EnabledByDefault from settings).
        registry.AddCollectionRule(new PinupGameMatchRule(settings.ConfigurationChecks.PinupGameMatch));
        registry.AddCollectionRule(new PinupMetadataCheckRule(settings.ConfigurationChecks.PinupMetadataCheck));
        registry.AddCollectionRule(new PinupMediaMatchRule(settings.ConfigurationChecks.MediaMatch));
        registry.AddCollectionRule(new VrRoomMatchRule(settings.ConfigurationChecks.VrRoomMatching));
        registry.AddCollectionRule(new DofLookupRule(settings.DofConfigPath));
        registry.AddCollectionRule(new PupHygieneRule(settings.ConfigurationChecks.PupHygiene));
        registry.AddCollectionRule(new VersionCheckRule(settings.ConfigurationChecks.VersionCheck));

        // 4. Built-in code rule: duplicate cGameName across the collection.
        registry.AddCollectionRule(new DuplicateGameNameRule());

        // 5. Built-in table rule: table name follows "Name (Manufacturer Year)".
        registry.AddTableRule(new WellFormedNameRule());

        // 6. Built-in table rule: ball shadow primitives must not hide parts behind.
        registry.AddTableRule(new BallShadowDepthMaskRule());

        // 7. Built-in table rule: detect the original ninuzzu ball shadow routine.
        registry.AddTableRule(new BallShadowRoutineRule());

        // 8. Built-in table rule: PostItNote image alpha mask should be high enough.
        registry.AddTableRule(new PostItNoteAlphaMaskRule());

        return registry;
    }
}
