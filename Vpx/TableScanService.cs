using System.Text.RegularExpressions;
using VPX_Inspector.Vpx.Rules;

namespace VPX_Inspector.Vpx;

/// <summary>
/// Shared scanning engine used by both the console and the UI. Resolves the set
/// of .vpx files to scan and evaluates the rule set against each one, returning
/// structured <see cref="TableResult"/> records instead of writing to output.
/// </summary>
public sealed class TableScanService
{
    private readonly RuleEngine _engine;
    private readonly IReadOnlySet<string>? _enabledRuleIds;
    private readonly InspectionSettings _settings;

    public TableScanService(
        RuleEngine engine,
        IReadOnlySet<string>? enabledRuleIds = null,
        InspectionSettings? settingsOverride = null)
    {
        _engine = engine;
        _enabledRuleIds = enabledRuleIds;
        _settings = settingsOverride ?? engine.Settings;
    }

    /// <summary>The rules backing this service, in file order.</summary>
    public IReadOnlyList<InspectionRule> Rules => _engine.Rules;

    /// <summary>General scan settings (max run time, exclude patterns).</summary>
    public InspectionSettings Settings => _settings;

    /// <summary>
    /// Resolves the .vpx files implied by an input path: a single .vpx file, or
    /// every .vpx under a folder (recursively). Files whose name matches any of
    /// <paramref name="excludePatterns"/> (globs) are omitted.
    /// </summary>
    public static IReadOnlyList<string> ResolveVpxFiles(
        string inputPath,
        out string? error,
        IReadOnlyList<string>? excludePatterns = null)
    {
        error = null;
        Regex? excludeRegex = BuildExcludeRegex(excludePatterns);

        if (Directory.Exists(inputPath))
        {
            return Directory
                .EnumerateFiles(inputPath, "*.vpx", SearchOption.AllDirectories)
                .Where(f => excludeRegex is null || !excludeRegex.IsMatch(Path.GetFileName(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (File.Exists(inputPath))
        {
            if (!string.Equals(Path.GetExtension(inputPath), ".vpx", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{inputPath}' is not a .vpx file.";
                return Array.Empty<string>();
            }

            // A directly-specified single file is honored even if it would match
            // an exclude pattern (the user asked for it explicitly).
            return new[] { inputPath };
        }

        error = $"Path not found: '{inputPath}'.";
        return Array.Empty<string>();
    }

    private static Regex? BuildExcludeRegex(IReadOnlyList<string>? excludePatterns)
    {
        if (excludePatterns is null || excludePatterns.Count == 0)
        {
            return null;
        }

        string combined = string.Join(
            "|",
            excludePatterns
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

    /// <summary>
    /// Scans a single .vpx file into a <see cref="TableResult"/>.
    /// </summary>
    public TableResult ScanFile(string vpxFile)
    {
        string tableName = Path.GetFileName(vpxFile);

        try
        {
            IReadOnlyList<GameItem> items = VpxCompoundFile.ScanGameItems(vpxFile);
            IReadOnlyList<RuleMatch> matches = _engine.Evaluate(items, _enabledRuleIds);

            string script = VpxCompoundFile.GetScript(vpxFile);
            string gameName = ScriptAnalyzer.ResolveGameName(
                script, Path.GetFileNameWithoutExtension(vpxFile));

            return new TableResult(tableName, vpxFile, Failed: false, matches)
            {
                GameName = gameName,
            };
        }
        catch (Exception ex)
        {
            return new TableResult(tableName, vpxFile, Failed: true, Array.Empty<RuleMatch>())
            {
                Error = ex.Message,
            };
        }
    }

    /// <summary>
    /// Scans a set of .vpx files, invoking <paramref name="onResult"/> after each
    /// one so callers can stream output or update a UI. Supports cancellation.
    /// </summary>
    public IReadOnlyList<TableResult> ScanFiles(
        IEnumerable<string> vpxFiles,
        Action<TableResult, int, int>? onResult = null,
        CancellationToken cancellationToken = default)
    {
        var files = vpxFiles.ToList();
        var results = new List<TableResult>(files.Count);

        double maxSeconds = Settings.MaxRunTimeSeconds;
        var stopwatch = maxSeconds > 0 ? System.Diagnostics.Stopwatch.StartNew() : null;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TableResult result = ScanFile(files[i]);
            results.Add(result);
            onResult?.Invoke(result, i + 1, files.Count);

            // Stop early once the optional time budget is exceeded.
            if (stopwatch is not null && stopwatch.Elapsed.TotalSeconds >= maxSeconds)
            {
                break;
            }
        }

        return results;
    }
}
