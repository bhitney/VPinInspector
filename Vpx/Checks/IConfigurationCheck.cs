using System.Text.RegularExpressions;

namespace VPX_Inspector.Vpx.Checks;

/// <summary>
/// Inputs available to a configuration check when it runs: the scanned path plus
/// helpers for enumerating .vpx files with or without the exclude filter.
/// </summary>
public sealed class ConfigurationCheckContext
{
    /// <summary>The path the user scanned (file or folder).</summary>
    public required string InputPath { get; init; }

    /// <summary>The active exclude globs (from settings/UI).</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>True when the current scan is a full folder scan (not rescan-flagged).</summary>
    public bool IsFullScan { get; init; }

    /// <summary>
    /// Resolves the folder to operate on: the input when it's a directory, or its
    /// parent when a single file was scanned. Null when neither exists.
    /// </summary>
    public string? ResolveFolder()
    {
        if (Directory.Exists(InputPath))
        {
            return InputPath;
        }

        string? parent = Path.GetDirectoryName(InputPath);
        return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : null;
    }

    /// <summary>
    /// Enumerates the top-level .vpx file names in <paramref name="folder"/>. When
    /// <paramref name="respectExcludePatterns"/> is true, files matching any active
    /// exclude glob are omitted; otherwise the raw filesystem listing is returned.
    /// </summary>
    public IReadOnlyList<string> EnumerateVpxFileNames(string folder, bool respectExcludePatterns)
    {
        Regex? exclude = respectExcludePatterns ? BuildExcludeRegex(ExcludePatterns) : null;

        return Directory
            .EnumerateFiles(folder, "*.vpx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Where(n => exclude is null || !exclude.IsMatch(n))
            .ToList();
    }

    private static Regex? BuildExcludeRegex(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        string combined = string.Join(
            "|",
            patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(GlobToRegex));

        return combined.Length == 0
            ? null
            : new Regex(combined, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GlobToRegex(string glob)
    {
        string escaped = Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".");
        return $"^(?:{escaped})$";
    }
}

/// <summary>
/// A collection-scope check that mashes up the filesystem with external state
/// (e.g. a database) rather than diving into individual table contents.
/// </summary>
public interface IConfigurationCheck
{
    /// <summary>Stable identifier, e.g. "pinup-game-match".</summary>
    string Id { get; }

    /// <summary>Human-readable description for the UI and reports.</summary>
    string Description { get; }

    /// <summary>Whether the check is enabled (per its settings).</summary>
    bool Enabled { get; }

    /// <summary>Runs the check and returns a renderable result.</summary>
    ConfigurationCheckResult Run(ConfigurationCheckContext context);
}
