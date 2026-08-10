using System.Text;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx;

/// <summary>
/// Produces the human-readable report text for scan results. Shared by the
/// console output and the UI so both render identically.
/// </summary>
public static class ReportFormatter
{
    /// <summary>Formats an interval, rendering negative values as a frame timer.</summary>
    public static string FormatInterval(int intervalMs) =>
        intervalMs < 0 ? "-1 (frame timer)" : $"{intervalMs}ms";

    /// <summary>Describes the proposed change for a matched element, if any.</summary>
    public static string DescribeProposal(InspectionRule rule, GameItem item)
    {
        if (rule.Suggest is not int suggested || !item.HasTimer)
        {
            return string.Empty;
        }

        return item.TimerIntervalMs == suggested
            ? "(at suggested)"
            : $"proposed -> {FormatInterval(suggested)}";
    }

    /// <summary>
    /// Formats the detailed per-table block (header + grouped matches).
    /// </summary>
    public static string FormatTableDetail(TableResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 90));
        sb.AppendLine($"TABLE: {result.TableName}");
        sb.AppendLine(new string('=', 90));

        if (result.Failed)
        {
            sb.AppendLine($"  ERROR reading table: {result.Error ?? "unknown error"}");
            return sb.ToString();
        }

        if (result.Matches.Count == 0)
        {
            sb.AppendLine("  No rule matches.");
            return sb.ToString();
        }

        foreach (var group in result.Matches.GroupBy(m => m.Rule.Id))
        {
            InspectionRule rule = group.First().Rule;
            string condition = string.IsNullOrWhiteSpace(rule.Interval) ? "(any)" : rule.Interval!;
            sb.AppendLine($"  [{rule.Id}] {rule.Description}  (interval {condition})");

            foreach (RuleMatch match in group.OrderBy(m => m.Item.TimerIntervalMs))
            {
                GameItem item = match.Item;
                string interval = item.HasTimer ? $"{item.TimerIntervalMs}ms" : "no timer";
                string proposal = DescribeProposal(rule, item);
                sb.AppendLine(
                    $"      - {item.Name,-30} {item.TypeName,-12} {interval,-10} enabled={item.TimerEnabled}  {proposal,-22} ({item.StreamName})");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the end-of-run summary checklist, totals, and unreadable list.
    /// </summary>
    public static string FormatSummary(IReadOnlyList<TableResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('#', 90));
        sb.AppendLine("SUMMARY CHECKLIST");
        sb.AppendLine(new string('#', 90));
        sb.AppendLine();

        int flaggedTables = results.Count(r => r.IsFlagged);
        int cleanTables = results.Count(r => r.IsClean);
        int failedTables = results.Count(r => r.Failed);
        int totalMatches = results.Sum(r => r.Matches.Count);

        foreach (TableResult result in results.OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (result.Failed)
            {
                sb.AppendLine($"[!] {result.TableName}  (could not be read)");
                continue;
            }

            if (result.Matches.Count == 0)
            {
                // Clean tables are omitted from the checklist to reduce noise.
                continue;
            }

            sb.AppendLine($"[ ] {result.TableName}  ({result.Matches.Count} match(es))");

            foreach (var group in result.Matches.GroupBy(m => m.Rule.Id))
            {
                InspectionRule rule = group.First().Rule;
                IEnumerable<string> names = group
                    .OrderBy(m => m.Item.TimerIntervalMs)
                    .Select(m =>
                    {
                        GameItem item = m.Item;
                        string current = item.HasTimer
                            ? $"{item.Name} [{item.TypeName}] ({item.TimerIntervalMs}ms)"
                            : $"{item.Name} [{item.TypeName}]";

                        if (rule.Suggest is int s && item.HasTimer && item.TimerIntervalMs != s)
                        {
                            current += $" -> {FormatInterval(s)}";
                        }

                        return current;
                    });

                sb.AppendLine($"      [ ] {rule.Id}: {string.Join(", ", names)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            $"Totals: {results.Count} table(s), {flaggedTables} flagged, {cleanTables} clean, " +
            $"{failedTables} unreadable, {totalMatches} total match(es).");

        var unreadable = results
            .Where(r => r.Failed)
            .OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unreadable.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(new string('#', 90));
            sb.AppendLine($"UNREADABLE TABLES ({unreadable.Count}) - please verify manually");
            sb.AppendLine(new string('#', 90));
            sb.AppendLine();

            foreach (TableResult result in unreadable)
            {
                sb.AppendLine($"[!] {result.TableName}");
                sb.AppendLine($"      {result.FilePath}");
            }
        }

        // Duplicate cGameName groups (2+ tables sharing a game name).
        var duplicateGroups = results
            .Where(r => !r.Failed && !string.IsNullOrWhiteSpace(r.GameName))
            .GroupBy(r => r.GameName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(new string('#', 90));
            sb.AppendLine($"DUPLICATE cGameName ({duplicateGroups.Count}) - tables sharing a game name");
            sb.AppendLine(new string('#', 90));
            sb.AppendLine();

            foreach (var group in duplicateGroups)
            {
                IEnumerable<string> tables = group
                    .Select(r => r.TableName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                sb.AppendLine($"{group.Key}: {string.Join(", ", tables)}");
            }
        }

        return sb.ToString();
    }
}
