using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Rules;

/// <summary>
/// Collection rule: flags groups of 2+ tables that share the same resolved game
/// name (cGameName). Duplicates usually indicate an accidental copy or a ROM
/// collision. Operates purely on the already-loaded tables in the context, so it
/// needs no filesystem or database access. Ported from the duplicate-cGameName
/// section previously computed in ReportFormatter.FormatSummary.
/// </summary>
public sealed class DuplicateGameNameRule : ICollectionRule
{
    public string Id => "duplicate-game-name";

    public string Description => "Duplicate cGameName (tables sharing a game name)";

    public bool EnabledByDefault => true;

    public IReadOnlySet<string> SupportedPlatforms { get; } = new HashSet<string>();

    public IEnumerable<Finding> Evaluate(CollectionContext context)
    {
        var duplicateGroups = context.Tables
            .Select(t => t.Table)
            .Where(t => !string.IsNullOrWhiteSpace(t.GameName))
            .GroupBy(t => t.GameName!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in duplicateGroups)
        {
            IEnumerable<string> tables = group
                .Select(t => t.TableName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            yield return new Finding(
                Id,
                FindingSeverity.Warning,
                $"{group.Key}: {string.Join(", ", tables)}");
        }
    }
}
