using System.Text.RegularExpressions;

namespace VPin.Inspector.Vpx;

/// <summary>
/// Lightweight parsing of a table's VBScript for values of interest.
/// </summary>
public static partial class ScriptAnalyzer
{
    // Matches an assignment of cGameName to a quoted string, tolerant of the
    // declaration keyword (Const/Dim/Public/Private/none) and spacing, and of
    // the assignment appearing inline rather than at the start of a line, e.g.
    //   Const cGameName = "myrom"
    //   cGameName = "myrom"
    //   Private Const cGameName="myrom"
    //   If RomSet = 1 then cGameName="blckhole": ... : End If
    // For dynamic tables (multiple assignments) the first uncommented one wins.
    [GeneratedRegex(
        "(?im)cGameName\\s*=\\s*\"([^\"]*)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex CGameNameRegex();

    /// <summary>
    /// Extracts the cGameName value from script text, ignoring commented-out
    /// lines. Returns null when no (uncommented) definition is found.
    /// </summary>
    public static string? FindCGameName(string script)
    {
        if (string.IsNullOrEmpty(script))
        {
            return null;
        }

        foreach (Match match in CGameNameRegex().Matches(script))
        {
            // Skip matches on lines that are commented out with a leading '.
            int lineStart = script.LastIndexOf('\n', Math.Min(match.Index, script.Length - 1));
            int prefixStart = lineStart + 1;
            int prefixLength = match.Index - prefixStart;
            if (prefixLength > 0)
            {
                string linePrefix = script.Substring(prefixStart, prefixLength);
                if (linePrefix.TrimStart().StartsWith('\''))
                {
                    continue;
                }
            }

            string value = match.Groups[1].Value.Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the effective game name for a table: the cGameName from the
    /// script when present, otherwise the fallback (typically the table name).
    /// </summary>
    public static string ResolveGameName(string script, string fallback)
    {
        string? found = FindCGameName(script);
        return string.IsNullOrWhiteSpace(found) ? fallback : found!;
    }

    // Matches any double-quoted string literal (VBScript strings use double
    // quotes). Used to find image names that the script assembles dynamically.
    [GeneratedRegex("\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteralRegex();

    /// <summary>
    /// Determines which of the supplied image <paramref name="names"/> the
    /// script references, so a usage check never flags a script-driven image as
    /// unused. Matching is deliberately conservative (it errs toward "used"):
    /// <list type="bullet">
    /// <item>Identifier-safe names (letters/digits/underscore) match on a whole
    /// word boundary, so "logo" doesn't match "logomania".</item>
    /// <item>Names containing spaces or punctuation match as a plain substring,
    /// which is safe because such names rarely occur by accident.</item>
    /// <item>Prefix-family guard: if the script contains a string literal that is
    /// a prefix of an image name (e.g. literal <c>"postit"</c> for image
    /// <c>postit3</c>), the name is treated as referenced. This catches names
    /// assembled at runtime, e.g. <c>EVAL("postit" &amp; n)</c>, which cannot be
    /// resolved statically.</item>
    /// </list>
    /// Matching is case-insensitive, matching VPX's case-insensitive names.
    /// </summary>
    public static ISet<string> FindReferencedImageNames(
        string? script, IReadOnlyCollection<string> names)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(script) || names.Count == 0)
        {
            return referenced;
        }

        string scriptLower = script.ToLowerInvariant();

        // String literals the script builds names from (for the prefix guard).
        var literals = new List<string>();
        foreach (Match m in QuotedLiteralRegex().Matches(script))
        {
            string literal = m.Groups[1].Value;
            if (literal.Length >= 3)
            {
                literals.Add(literal.ToLowerInvariant());
            }
        }

        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name) || referenced.Contains(name))
            {
                continue;
            }

            string nameLower = name.ToLowerInvariant();

            bool identifierSafe = IsIdentifierSafe(name);
            bool direct = identifierSafe
                ? Regex.IsMatch(scriptLower,
                    $"\\b{Regex.Escape(nameLower)}\\b",
                    RegexOptions.CultureInvariant)
                : scriptLower.Contains(nameLower, StringComparison.Ordinal);

            if (direct)
            {
                referenced.Add(name);
                continue;
            }

            // Prefix-family guard for dynamically assembled names.
            foreach (string literal in literals)
            {
                if (literal.Length < nameLower.Length &&
                    nameLower.StartsWith(literal, StringComparison.Ordinal))
                {
                    referenced.Add(name);
                    break;
                }
            }
        }

        return referenced;
    }

    private static bool IsIdentifierSafe(string name)
    {
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
