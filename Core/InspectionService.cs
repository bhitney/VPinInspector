using System.Diagnostics;
using System.Text.RegularExpressions;
using VPin.Inspector.Core.Platforms;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Core;

/// <summary>
/// Options controlling a scan: rule selection, exclude globs, an optional
/// wall-clock budget, and an optional explicit file set (for "rescan flagged").
/// </summary>
public sealed class ScanOptions
{
    /// <summary>Per-run rule selection; null honors each rule's EnabledByDefault.</summary>
    public IReadOnlySet<string>? SelectedRuleIds { get; init; }

    /// <summary>File-name globs to exclude from folder discovery.</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// File-name globs a table must match to be included in folder discovery.
    /// Empty = include everything. Exclude patterns still take precedence.
    /// </summary>
    public IReadOnlyList<string> IncludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>Optional wall-clock budget in seconds; zero/absent = no limit.</summary>
    public double MaxRunTimeSeconds { get; init; }

    /// <summary>
    /// Optional global minimum .vpx file size in megabytes. When greater than
    /// zero, files smaller than this are dropped during discovery (never parsed).
    /// Zero/absent = no size filter.
    /// </summary>
    public int MinTableSizeMB { get; init; }

    /// <summary>
    /// Maximum number of tables parsed concurrently. Zero or negative = use
    /// <see cref="Environment.ProcessorCount"/>; 1 = single-threaded. The report
    /// order is independent of this value.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// When set, exactly these files are scanned (folder discovery is skipped).
    /// Used to rescan a subset such as the previously-flagged tables.
    /// </summary>
    public IReadOnlyList<string>? ExplicitFiles { get; init; }

    /// <summary>
    /// When false, collection rules are skipped (they need the full collection).
    /// </summary>
    public bool RunCollectionRules { get; init; } = true;

    /// <summary>
    /// File names (e.g. "Table.vpx", case-insensitive) the user has chosen to
    /// hide from scans. Matching files are excluded from discovery and counted
    /// as skipped. Null or empty = hide nothing.
    /// </summary>
    public IReadOnlySet<string>? HiddenFileNames { get; init; }

    /// <summary>
    /// When true, folder discovery descends into subdirectories. Defaults to
    /// false so only the top-level tables folder is scanned.
    /// </summary>
    public bool Recursive { get; init; }
}

/// <summary>
/// Scope-agnostic engine that replaces the VPX-specific TableScanService. Loads
/// each table once through its platform, runs the selected table rules per-table
/// (streaming results back), then the selected collection rules over the whole
/// set, returning a structured <see cref="ScanReport"/>.
/// </summary>
public sealed class InspectionService
{
    private readonly InspectionRegistry _registry;

    public InspectionService(InspectionRegistry registry) => _registry = registry;

    /// <summary>Resolves the files a scan would cover for the given input/options.</summary>
    public IReadOnlyList<string> ResolveFiles(string inputPath, ScanOptions? options = null)
    {
        options ??= new ScanOptions();

        if (options.ExplicitFiles is not null)
        {
            return options.ExplicitFiles;
        }

        if (options.ExplicitFiles is not null)
        {
            return ApplyHiddenFilter(options.ExplicitFiles, options.HiddenFileNames);
        }

        Regex? exclude = BuildExcludeRegex(options.ExcludePatterns);
        Regex? include = BuildExcludeRegex(options.IncludePatterns);

        if (Directory.Exists(inputPath))
        {
            var extensions = _registry.AllFileExtensions
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var files = Directory
                .EnumerateFiles(
                    inputPath,
                    "*.*",
                    options.Recursive
                        ? SearchOption.AllDirectories
                        : SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .Where(f => include is null || include.IsMatch(Path.GetFileName(f)))
                .Where(f => exclude is null || !exclude.IsMatch(Path.GetFileName(f)))
                .Where(f => PassesMinSize(f, options.MinTableSizeMB))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ApplyHiddenFilter(files, options.HiddenFileNames);
        }

        if (File.Exists(inputPath))
        {
            return ApplyHiddenFilter(new[] { inputPath }, options.HiddenFileNames);
        }

        return Array.Empty<string>();
    }

    private static List<string> ApplyHiddenFilter(
        IReadOnlyList<string> files, IReadOnlySet<string>? hiddenFileNames)
    {
        if (hiddenFileNames is null || hiddenFileNames.Count == 0)
        {
            return files.ToList();
        }

        return files
            .Where(f => !hiddenFileNames.Contains(Path.GetFileName(f)))
            .ToList();
    }

    /// <summary>
    /// True when the file is at least <paramref name="minMb"/> megabytes, or when
    /// no size filter is set (<paramref name="minMb"/> &lt;= 0). A file whose size
    /// can't be read passes, so a transient I/O issue never silently hides it.
    /// </summary>
    private static bool PassesMinSize(string path, int minMb)
    {
        if (minMb <= 0)
        {
            return true;
        }

        try
        {
            return new FileInfo(path).Length >= (long)minMb * 1024 * 1024;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Scans an input path into a <see cref="ScanReport"/>. Invokes
    /// <paramref name="onTable"/> after each table so callers can stream output
    /// or update a UI. Honors cancellation and the optional time budget.
    /// </summary>
    public ScanReport Scan(
        string inputPath,
        ScanOptions? options = null,
        Action<TableReport, int, int>? onTable = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();

        var files = ResolveFiles(inputPath, options);

        // Count how many tables were hidden (present without the filter, absent
        // with it) so the report can surface it as a data point.
        int skippedTableCount = 0;
        if (options.HiddenFileNames is { Count: > 0 })
        {
            var unfiltered = ResolveFiles(
                inputPath,
                new ScanOptions
                {
                    SelectedRuleIds = options.SelectedRuleIds,
                    ExcludePatterns = options.ExcludePatterns,
                    IncludePatterns = options.IncludePatterns,
                    MaxRunTimeSeconds = options.MaxRunTimeSeconds,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    ExplicitFiles = options.ExplicitFiles,
                    RunCollectionRules = options.RunCollectionRules,
                    HiddenFileNames = null,
                    Recursive = options.Recursive,
                    MinTableSizeMB = options.MinTableSizeMB,
                });
            skippedTableCount = unfiltered.Count - files.Count;
        }

        // Only pay for the expensive body parse when a selected rule needs it.
        bool needsDeep = RequiresDeepAnalysis(options);

        // Results are written by index so the report order matches file order
        // (alphabetical), regardless of the order tables finish in parallel.
        var reportsByIndex = new TableReport?[files.Count];
        var contextsByIndex = new TableContext?[files.Count];

        Stopwatch? stopwatch = options.MaxRunTimeSeconds > 0
            ? Stopwatch.StartNew()
            : null;

        int degree = options.MaxDegreeOfParallelism > 0
            ? options.MaxDegreeOfParallelism
            : Environment.ProcessorCount;

        int completed = 0;
        object gate = new();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, degree),
            CancellationToken = cancellationToken,
        };

        Parallel.For(0, files.Count, parallelOptions, (i, state) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch is not null && stopwatch.Elapsed.TotalSeconds >= options.MaxRunTimeSeconds)
            {
                state.Stop();
                return;
            }

            TableReport report = ScanSingle(files[i], options.SelectedRuleIds, needsDeep, out TableContext? context);
            reportsByIndex[i] = report;
            contextsByIndex[i] = context;

            // The streaming callback fires as each table finishes (log order may
            // interleave); the final ordered report is assembled below.
            if (onTable is not null)
            {
                lock (gate)
                {
                    completed++;
                    onTable(report, completed, files.Count);
                }
            }

            if (stopwatch is not null && stopwatch.Elapsed.TotalSeconds >= options.MaxRunTimeSeconds)
            {
                state.Stop();
            }
        });

        // Assemble in file order, skipping any slots left empty by an early stop.
        var tableReports = new List<TableReport>(files.Count);
        var contexts = new List<TableContext>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            if (reportsByIndex[i] is { } report)
            {
                tableReports.Add(report);
            }

            if (contextsByIndex[i] is { } context)
            {
                contexts.Add(context);
            }
        }

        var collectionGroups = new List<CollectionFindingGroup>();
        if (options.RunCollectionRules)
        {
            var collectionContext = new CollectionContext
            {
                InputPath = inputPath,
                Tables = contexts,
                Platforms = _registry.Platforms,
                ExcludePatterns = options.ExcludePatterns,
                IncludePatterns = options.IncludePatterns,
            };

            foreach (ICollectionRule rule in _registry.CollectionRules)
            {
                if (!IsSelected(rule, options.SelectedRuleIds))
                {
                    continue;
                }

                var findings = rule.Evaluate(collectionContext).ToList();
                collectionGroups.Add(new CollectionFindingGroup
                {
                    RuleId = rule.Id,
                    Description = rule.Description,
                    Findings = findings,
                });
            }
        }

        return new ScanReport
        {
            InputPath = inputPath,
            Tables = tableReports,
            CollectionFindings = collectionGroups,
            SkippedTableCount = skippedTableCount,
        };
    }

    private TableReport ScanSingle(
        string filePath,
        IReadOnlySet<string>? selectedRuleIds,
        bool deep,
        out TableContext? context)
    {
        context = null;
        string tableName = Path.GetFileName(filePath);

        IPinballPlatform? platform = _registry.ResolvePlatform(filePath);
        if (platform is null)
        {
            return new TableReport
            {
                TableName = tableName,
                FilePath = filePath,
                Failed = true,
                Error = "No registered platform can read this file.",
            };
        }

        try
        {
            var table = deep ? platform.Load(filePath) : platform.LoadShallow(filePath);
            var ctx = new TableContext { Platform = platform, Table = table };
            context = ctx;

            var findings = new List<Finding>();
            foreach (ITableRule rule in _registry.TableRules)
            {
                if (!IsSelected(rule, selectedRuleIds))
                {
                    continue;
                }

                // Platform gate: empty SupportedPlatforms = agnostic.
                if (rule.SupportedPlatforms.Count > 0 &&
                    !rule.SupportedPlatforms.Contains(platform.Id))
                {
                    continue;
                }

                findings.AddRange(rule.Evaluate(ctx));
            }

            return new TableReport
            {
                TableName = tableName,
                FilePath = filePath,
                Failed = false,
                GameName = table.GameName ?? Path.GetFileNameWithoutExtension(filePath),
                Table = table,
                Findings = findings,
            };
        }
        catch (Exception ex)
        {
            return new TableReport
            {
                TableName = tableName,
                FilePath = filePath,
                Failed = true,
                Error = ex.Message,
            };
        }
    }

    private static Regex? BuildExcludeRegex(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        string combined = string.Join(
            "|",
            patterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(GlobToRegex));

        return combined.Length == 0
            ? null
            : new Regex(combined, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GlobToRegex(string glob)
    {
        string escaped = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^(?:{escaped})$";
    }

    private static bool IsSelected(IInspectionRule rule, IReadOnlySet<string>? selectedRuleIds) =>
        selectedRuleIds is not null
            ? selectedRuleIds.Contains(rule.Id)
            : rule.EnabledByDefault;

    /// <summary>
    /// True when any selected rule (table, or collection when they run) needs a
    /// full table parse. When false, tables are loaded shallowly (metadata only).
    /// </summary>
    private bool RequiresDeepAnalysis(ScanOptions options)
    {
        bool tableDeep = _registry.TableRules
            .Any(r => r.Depth == AnalysisDepth.Deep && IsSelected(r, options.SelectedRuleIds));

        bool collectionDeep = options.RunCollectionRules && _registry.CollectionRules
            .Any(r => r.Depth == AnalysisDepth.Deep && IsSelected(r, options.SelectedRuleIds));

        return tableDeep || collectionDeep;
    }
}
