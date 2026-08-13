using System.Text.RegularExpressions;
using VPin.Inspector.Core.Platforms;

namespace VPin.Inspector.Core.Rules;

/// <summary>
/// Inputs for a collection-scope rule: the scanned path plus the loaded tables
/// (already parsed once by the core) and the active platforms. This is where
/// folder-vs-external-state modules live — DOF, PinUP Popper, VPS version
/// checks — each of which is opt-in like any other rule.
/// </summary>
public sealed class CollectionContext
{
    /// <summary>The path the user scanned (a file or a folder).</summary>
    public required string InputPath { get; init; }

    /// <summary>Every table successfully loaded during this scan.</summary>
    public required IReadOnlyList<TableContext> Tables { get; init; }

    /// <summary>Platforms that participated in this scan.</summary>
    public IReadOnlyList<IPinballPlatform> Platforms { get; init; }
        = Array.Empty<IPinballPlatform>();

    /// <summary>The active exclude globs (from settings/UI).</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

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

/// <summary>A rule that inspects the whole scanned collection once.</summary>
public interface ICollectionRule : IInspectionRule
{
    IEnumerable<Finding> Evaluate(CollectionContext context);
}
