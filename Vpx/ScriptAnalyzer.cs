using System.Text.RegularExpressions;

namespace VPX_Inspector.Vpx;

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
}
