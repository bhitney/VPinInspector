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
/// Dynamic filter applied to the summary checklist based on cross-referenced
/// PinUP info.
/// </summary>
/// <param name="MinRating">
/// Minimum game rating (inclusive) a table must have to be shown. 0 disables the
/// rating filter (all tables pass, including those with no/unknown rating).
/// </param>
/// <param name="Visibilities">
/// The set of visibility codes to include (use
/// <see cref="PinupVisibilityLookup.UnknownVisibility"/> for not-matched
/// tables). Null or empty means "all" (no visibility filtering).
/// </param>
/// <param name="RuleIds">
/// The set of rule ids a table must have flagged at least one of to be shown.
/// Null or empty means "all" (no rule filtering). A table still lists every rule
/// it violated; this only gates whether the table is included.
/// </param>
public readonly record struct SummaryFilter(
    int MinRating,
    IReadOnlySet<int>? Visibilities,
    IReadOnlySet<string>? RuleIds = null)
{
    /// <summary>
    /// Returns true when the given cross-reference (may be null for unmatched
    /// tables) and the table's flagged rule ids satisfy this filter.
    /// </summary>
    public bool Matches(PinupCrossRef? crossRef, IReadOnlySet<string> flaggedRuleIds)
    {
        if (MinRating > 0)
        {
            // Tables with no rating (null) never satisfy an explicit minimum.
            if (crossRef?.Rating is not { } rating || rating < MinRating)
            {
                return false;
            }
        }

        if (Visibilities is { Count: > 0 } wanted)
        {
            int actual = crossRef?.Visibility ?? PinupVisibilityLookup.UnknownVisibility;
            if (!wanted.Contains(actual))
            {
                return false;
            }
        }

        if (RuleIds is { Count: > 0 } wantedRules)
        {
            // The table must have flagged at least one of the selected rules.
            if (!wantedRules.Overlaps(flaggedRuleIds))
            {
                return false;
            }
        }

        return true;
    }
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
    public static string FormatSummary(
        ScanReport report,
        SummarySort sort = SummarySort.Alphabetical,
        IReadOnlyDictionary<string, PinupCrossRef>? crossRefByFileName = null,
        SummaryFilter? filter = null)
    {
        var sb = new StringBuilder();
        foreach (RenderedLine line in BuildSummaryLines(report, sort, crossRefByFileName, filter))
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
    /// <param name="crossRefByFileName">
    /// Optional map of table file name (no path) to cross-referenced PinUP info
    /// (visibility status label + rating). When supplied, each flagged table
    /// headline is prefixed with "[Status] [Rating: N] " when a match is found.
    /// </param>
    /// <param name="filter">
    /// Optional summary filter (minimum rating and/or exact visibility). Tables
    /// that don't satisfy the filter are omitted from the checklist.
    /// </param>
    public static IReadOnlyList<RenderedLine> BuildSummaryLines(
        ScanReport report,
        SummarySort sort = SummarySort.Alphabetical,
        IReadOnlyDictionary<string, PinupCrossRef>? crossRefByFileName = null,
        SummaryFilter? filter = null)
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

            // Apply the optional summary filter (min rating / visibility / rule
            // ids). Tables that don't satisfy it are omitted.
            if (filter is { } activeFilter)
            {
                var flaggedRuleIds = table.Findings
                    .Select(f => f.RuleId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!activeFilter.Matches(ResolveCrossRef(table, crossRefByFileName), flaggedRuleIds))
                {
                    continue;
                }
            }

            // The table headline takes the table's overall (max) severity. The
            // count reflects the number of distinct rules violated, not the raw
            // number of flagged items (which can be dominated by one rule).
            int ruleCount = RuleViolationCount(table);
            string visibilityTag = ResolveCrossRefTag(table, crossRefByFileName);
            Line(table.Severity, $"[ ] {visibilityTag}{table.TableName}  ({ruleCount} rule(s) flagged)");
            foreach (var group in table.Findings.GroupBy(f => f.RuleId))
            {
                FindingSeverity groupSeverity = group.Max(f => f.Severity);
                IEnumerable<string> names = group.Select(f =>
                    f.Element is not null ? DescribeElementShort(f.Element) : f.Message);
                // Mark auto-fixable rules with a '*' inside the checkbox so users
                // can tell at a glance which findings the "fix" action addresses.
                string ruleMarker = VpxCorrectionWriter.FixableRuleIds.Contains(group.Key) ? "[*]" : "[ ]";
                Line(groupSeverity, $"      {ruleMarker} {group.Key}: {string.Join(", ", names)}");
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

    /// <summary>
    /// Returns the cross-referenced PinUP entry for a table (matched by file
    /// name), or null when no map is supplied or the table isn't matched.
    /// </summary>
    private static PinupCrossRef? ResolveCrossRef(
        TableReport table, IReadOnlyDictionary<string, PinupCrossRef>? crossRefByFileName)
    {
        if (crossRefByFileName is null || crossRefByFileName.Count == 0)
        {
            return null;
        }

        string fileName = Path.GetFileName(table.FilePath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = table.TableName;
        }

        return crossRefByFileName.TryGetValue(fileName, out PinupCrossRef? crossRef)
            ? crossRef
            : null;
    }

    /// <summary>
    /// Returns the "[Status] [Rating: N] " prefix for a table when a PinUP
    /// cross-reference map is supplied and the table's file name matches a Games
    /// entry; otherwise empty. Rating shows "n/a" when null.
    /// </summary>
    private static string ResolveCrossRefTag(
        TableReport table, IReadOnlyDictionary<string, PinupCrossRef>? crossRefByFileName)
    {
        PinupCrossRef? crossRef = ResolveCrossRef(table, crossRefByFileName);
        if (crossRef is null)
        {
            return string.Empty;
        }

        string rating = crossRef.Rating is { } r ? r.ToString() : "n/a";
        return $"[{crossRef.StatusLabel}] [Rating: {rating}] ";
    }

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
