using VPin.Inspector.Core.Rules;
using VPin.Inspector.Vpx.Dof; // reuse existing DofConfig loader

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Opt-in collection rule that checks each table's resolved game/ROM name against
/// a DirectOutput (DOF) config. Demonstrates that an integration nobody is forced
/// to use is just another rule: a user who doesn't run DOF never enables it.
///
/// This is a straight re-home of the logic previously special-cased inside
/// TableScanService.EvaluateDof.
/// </summary>
public sealed class DofLookupRule : ICollectionRule
{
    private readonly string? _configPath;

    public DofLookupRule(string? configPathOverride = null)
    {
        _configPath = DofConfig.ResolveConfigPath(configPathOverride ?? string.Empty);
    }

    public string Id => "dof-lookup";

    public string Description => "Checks each table's ROM name against the DOF config.";

    // Opt-in: off unless the user selects it.
    public bool EnabledByDefault => false;

    // Deep: needs each table's resolved ROM/cGameName from the parsed script.
    public AnalysisDepth Depth => AnalysisDepth.Deep;

    public IReadOnlySet<string> SupportedPlatforms { get; } =
        new HashSet<string> { "vpx" };

    public IEnumerable<Finding> Evaluate(CollectionContext context)
    {
        if (_configPath is null || !File.Exists(_configPath))
        {
            yield return new Finding(
                Id, FindingSeverity.Info, "DOF config not found; check skipped.");
            yield break;
        }

        DofConfig? config = null;
        try
        {
            config = DofConfig.LoadFromFile(_configPath);
        }
        catch
        {
            // Fall through: reported below as a skipped check.
        }

        if (config is null)
        {
            yield return new Finding(
                Id, FindingSeverity.Info, "DOF config could not be read; check skipped.");
            yield break;
        }

        foreach (TableContext table in context.Tables)
        {
            string? rom = table.Table.GameName;
            if (string.IsNullOrWhiteSpace(rom))
            {
                continue;
            }

            if (!config.HasRom(rom))
            {
                yield return new Finding(
                    Id,
                    FindingSeverity.Info,
                    $"'{table.Table.TableName}': no DOF entry for ROM '{rom}'.")
                {
                    Details = new Dictionary<string, string>
                    {
                        ["TableName"] = table.Table.TableName,
                        ["FilePath"] = table.Table.FilePath,
                    },
                };
            }
        }
    }
}
