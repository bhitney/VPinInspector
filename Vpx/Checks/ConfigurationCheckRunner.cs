using VPX_Inspector.Vpx.Pinup;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx.Checks;

/// <summary>
/// Builds the set of configuration checks from settings and runs the selected
/// ones. New configuration checks are registered here.
/// </summary>
public static class ConfigurationCheckRunner
{
    /// <summary>
    /// Constructs every configuration check from the given settings, in display
    /// order. Each check reports its own <see cref="IConfigurationCheck.Enabled"/>.
    /// </summary>
    public static IReadOnlyList<IConfigurationCheck> BuildChecks(InspectionSettings settings)
    {
        ConfigurationChecksSettings config = settings.ConfigurationChecks;

        return new IConfigurationCheck[]
        {
            new PinupGameMatchCheck(config.PinupGameMatch),
            // Future configuration checks are added here.
        };
    }

    /// <summary>
    /// Runs the checks whose id is in <paramref name="selectedIds"/> (or all
    /// enabled checks when null), returning their results.
    /// </summary>
    public static IReadOnlyList<ConfigurationCheckResult> Run(
        InspectionSettings settings,
        ConfigurationCheckContext context,
        IReadOnlySet<string>? selectedIds = null)
    {
        var results = new List<ConfigurationCheckResult>();

        foreach (IConfigurationCheck check in BuildChecks(settings))
        {
            bool selected = selectedIds is not null
                ? selectedIds.Contains(check.Id)
                : check.Enabled;

            if (!selected)
            {
                continue;
            }

            results.Add(check.Run(context));
        }

        return results;
    }
}
