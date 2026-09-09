using System.Text;
using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx.Reporting;

/// <summary>How the summary checklist orders its flagged tables.</summary>
public enum SummarySort
{
    /// <summary>Alphabetical by table name (the default).</summary>
    Alphabetical,

    /// <summary>Most findings first (ties broken alphabetically).</summary>
    FindingCountDescending,
}

/// <summary>
/// Renders a <see cref="ScanReport"/> to human-readable text for the console and
/// UI. Replaces the legacy ReportFormatter; consumes only neutral Core types.
/// </summary>
public static class ReportRenderer
{
    /// <summary>
    /// Text that precedes a table name on the auto-fix action line. The UI links
    /// the "<c>prefix + TableName</c>" span; keep it in sync with MainForm.
    /// </summary>
    public const string FixLinkPrefix = "[fix issues] ";

    /// <summary>
    /// Text that precedes a table name on the "hide" action line. The UI links
    /// the "<c>prefix + TableName</c>" span so a click adds the table to
    /// <c>hidden_tables.json</c>; keep it in sync with MainForm.
    /// </summary>
    public const string HideLinkPrefix = "[hide table] ";

    /// <summary>Formats the detailed per-table block (header + grouped findings).</summary>
    public static string FormatTableDetail(TableReport table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 90));
        sb.AppendLine($"TABLE: {table.TableName}");
        sb.AppendLine(new string('=', 90));

        if (table.Failed)
        {
            sb.AppendLine($"  ERROR reading table: {table.Error ?? "unknown error"}");
            return sb.ToString();
        }

        if (table.Findings.Count == 0)
        {
            sb.AppendLine("  No rule matches.");
            return sb.ToString();
        }

        foreach (var group in table.Findings.GroupBy(f => f.RuleId))
        {
            sb.AppendLine($"  [{group.Key}]");
            foreach (Finding finding in group)
            {
                string detail = DescribeElement(finding.Element);
                sb.AppendLine(
                    string.IsNullOrEmpty(detail)
                        ? $"      - {finding.Message}"
                        : $"      - {finding.Message}   {detail}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Formats the end-of-run summary checklist, totals, and collection findings.</summary>
    public static string FormatSummary(ScanReport report, SummarySort sort = SummarySort.Alphabetical)
    {
        var sb = new StringBuilder();
        foreach (RenderedLine line in BuildSummaryLines(report, sort))
        {
            sb.AppendLine(line.Text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the summary as severity-tagged lines. The console flattens these to
    /// text; the UI colors them. This is the single source of truth for summary
    /// content so both front-ends stay consistent.
    /// </summary>
    public static IReadOnlyList<RenderedLine> BuildSummaryLines(
        ScanReport report, SummarySort sort = SummarySort.Alphabetical)
    {
        var lines = new List<RenderedLine>();
        void Info(string t) => lines.Add(RenderedLine.Info(t));
        void Line(FindingSeverity s, string t) => lines.Add(new RenderedLine(s, t));

        Info(new string('#', 90));
        Info("SUMMARY CHECKLIST");
        Info(new string('#', 90));
        Info(string.Empty);

        IReadOnlyList<TableReport> results = report.Tables;
        int flaggedTables = results.Count(r => r.IsFlagged);
        int cleanTables = results.Count(r => r.IsClean);
        int failedTables = results.Count(r => r.Failed);
        int totalFindings = results.Sum(r => r.Findings.Count);

        IEnumerable<TableReport> ordered = sort == SummarySort.FindingCountDescending
            ? results
                .OrderByDescending(RuleViolationCount)
                .ThenBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            : results.OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase);

        foreach (TableReport table in ordered)
        {
            if (table.Failed)
            {
                Line(FindingSeverity.Error, $"[!] {table.TableName}  (could not be read)");
                continue;
            }

            if (table.Findings.Count == 0)
            {
                continue; // clean tables omitted to reduce noise
            }

            // The table headline takes the table's overall (max) severity. The
            // count reflects the number of distinct rules violated, not the raw
            // number of flagged items (which can be dominated by one rule).
            int ruleCount = RuleViolationCount(table);
            Line(table.Severity, $"[ ] {table.TableName}  ({ruleCount} rule(s) flagged)");
            foreach (var group in table.Findings.GroupBy(f => f.RuleId))
            {
                FindingSeverity groupSeverity = group.Max(f => f.Severity);
                IEnumerable<string> names = group.Select(f =>
                    f.Element is not null ? DescribeElementShort(f.Element) : f.Message);
                Line(groupSeverity, $"      [ ] {group.Key}: {string.Join(", ", names)}");
            }

            // A "fix" action line closes out the table's issue list when the
            // table has at least one auto-fixable finding. The UI turns the
            // "<prefix><TableName>" span into a clickable link.
            if (VpxCorrectionWriter.HasFixableFinding(table))
            {
                Line(table.Severity, $"      {FixLinkPrefix}{table.TableName}");
            }

            // A "hide" action line lets the user suppress this table from future
            // scans. The UI turns the "<prefix><TableName>" span into a link.
            Line(table.Severity, $"      {HideLinkPrefix}{table.TableName}");
        }

        Info(string.Empty);
        int collectionFindings = report.CollectionFindings.Sum(g => g.Findings.Count);
        Info(
            $"Totals: {results.Count} table(s), {flaggedTables} flagged, {cleanTables} clean, " +
            $"{failedTables} unreadable, {totalFindings} per-table finding(s), " +
            $"{collectionFindings} collection finding(s).");

        if (report.SkippedTableCount > 0)
        {
            Info($"Skipped {report.SkippedTableCount} hidden table(s) (see {HiddenTablesStore.FileName}).");
        }

        AppendUnreadableLines(lines, results);
        AppendCollectionFindingLines(lines, report.CollectionFindings);

        return lines;
    }

    private static void AppendUnreadableLines(List<RenderedLine> lines, IReadOnlyList<TableReport> results)
    {
        var unreadable = results
            .Where(r => r.Failed)
            .OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unreadable.Count == 0)
        {
            return;
        }

        lines.Add(RenderedLine.Info(string.Empty));
        lines.Add(RenderedLine.Info(new string('#', 90)));
        lines.Add(RenderedLine.Info($"UNREADABLE TABLES ({unreadable.Count}) - please verify manually"));
        lines.Add(RenderedLine.Info(new string('#', 90)));
        lines.Add(RenderedLine.Info(string.Empty));

        foreach (TableReport table in unreadable)
        {
            lines.Add(new RenderedLine(FindingSeverity.Error, $"[!] {table.TableName}"));
            lines.Add(new RenderedLine(FindingSeverity.Error, $"      {table.FilePath}"));
        }
    }

    private static void AppendCollectionFindingLines(
        List<RenderedLine> lines, IReadOnlyList<CollectionFindingGroup> groups)
    {
        foreach (CollectionFindingGroup group in groups)
        {
            lines.Add(RenderedLine.Info(string.Empty));
            lines.Add(RenderedLine.Info(new string('#', 90)));
            lines.Add(RenderedLine.Info(group.Description));
            lines.Add(RenderedLine.Info(new string('#', 90)));
            lines.Add(RenderedLine.Info(string.Empty));

            if (group.Findings.Count == 0)
            {
                lines.Add(RenderedLine.Info("    (no issues)"));
                continue;
            }

            foreach (Finding finding in group.Findings)
            {
                string tag = finding.Severity switch
                {
                    FindingSeverity.Error => "[ERROR]",
                    FindingSeverity.Warning => "[WARN]",
                    _ => "[INFO]",
                };
                lines.Add(new RenderedLine(finding.Severity, $"    {tag} {finding.Message}"));
            }
        }
    }

    /// <summary>
    /// Number of distinct rules a table violated (each rule counts once no
    /// matter how many elements it flagged), used for the headline count and
    /// the "most findings first" sort.
    /// </summary>
    private static int RuleViolationCount(TableReport table) =>
        table.Findings.Select(f => f.RuleId).Distinct(StringComparer.Ordinal).Count();

    private static string DescribeElement(TableElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        string interval = element is ITimerElement { HasTimer: true } timer
            ? $"{timer.TimerIntervalMs}ms"
            : "no timer";
        return $"[{element.TypeName}] {interval} ({element.Id})";
    }

    private static string DescribeElementShort(TableElement element)
    {
        if (element is ITimerElement { HasTimer: true } timer)
        {
            return $"{element.Name} [{element.TypeName}] ({timer.TimerIntervalMs}ms)";
        }

        return $"{element.Name} [{element.TypeName}]";
    }
}
