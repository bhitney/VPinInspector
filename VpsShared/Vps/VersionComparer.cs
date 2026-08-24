using System.Text.RegularExpressions;

namespace VPin.Inspector.Vps;

/// <summary>
/// The relationship between a local version (PinUP Popper database GAMEVER) and
/// an online version (VPS puplookup.csv GAMEVER).
/// </summary>
public enum VersionComparison
{
    /// <summary>Versions are effectively the same.</summary>
    Equal,

    /// <summary>The online (internet/VPS) version is newer than the local one.</summary>
    OnlineNewer,

    /// <summary>The local (PupDatabase) version is newer than the online one.</summary>
    LocalNewer,

    /// <summary>Versions differ but ordering can't be determined.</summary>
    Unknown,
}

/// <summary>
/// Best-effort comparison of the free-form version strings used by pinball
/// tables, e.g. "1.2b", "FizX3.3V1", "1.0.0f", "1.1", "v2.03". These aren't
/// strict semver, so the comparer extracts the dotted numeric backbone of each
/// string and compares those segment-by-segment; when the numeric backbones are
/// equal it falls back to an ordinal comparison of the remaining text. When it
/// can't confidently order two different strings it returns
/// <see cref="VersionComparison.Unknown"/> rather than guessing.
/// </summary>
public static partial class VersionComparer
{
    // Captures the first dotted numeric run, e.g. "3.3" in "FizX3.3V1",
    // "1.0.0" in "1.0.0f", "2.03" in "v2.03".
    [GeneratedRegex(@"\d+(?:\.\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumericBackboneRegex();

    /// <summary>
    /// Compares <paramref name="local"/> against <paramref name="online"/> and
    /// describes how the online version relates to the local one.
    /// </summary>
    public static VersionComparison Compare(string? local, string? online)
    {
        string l = Normalize(local);
        string o = Normalize(online);

        // Missing data can't be ordered.
        if (l.Length == 0 || o.Length == 0)
        {
            return l == o ? VersionComparison.Equal : VersionComparison.Unknown;
        }

        if (string.Equals(l, o, StringComparison.OrdinalIgnoreCase))
        {
            return VersionComparison.Equal;
        }

        Match lMatch = NumericBackboneRegex().Match(l);
        Match oMatch = NumericBackboneRegex().Match(o);

        int[]? lNums = ExtractBackbone(lMatch);
        int[]? oNums = ExtractBackbone(oMatch);

        if (lNums is not null && oNums is not null)
        {
            int cmp = CompareSegments(oNums, lNums); // online relative to local
            if (cmp > 0)
            {
                return VersionComparison.OnlineNewer;
            }

            if (cmp < 0)
            {
                return VersionComparison.LocalNewer;
            }

            // Numeric backbones tie. Missing trailing segments are treated as
            // zero, so "1.0" and "1.0.0" tie here. Treat that as Equal when the
            // non-numeric remainder also matches; otherwise the strings differ
            // only by a build/beta suffix (e.g. "1.0" vs "1.0f") which we can't
            // reliably order.
            return ResiduesMatch(l, lMatch, o, oMatch)
                ? VersionComparison.Equal
                : VersionComparison.Unknown;
        }

        // One or both lack any numeric backbone; treat as an unorderable diff.
        return VersionComparison.Unknown;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static int[]? ExtractBackbone(Match match)
    {
        if (!match.Success)
        {
            return null;
        }

        string[] parts = match.Value.Split('.');
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]))
            {
                return null;
            }
        }

        return numbers;
    }

    /// <summary>
    /// True when the text surrounding the numeric backbone is equivalent, so two
    /// versions that tie numerically (e.g. "1.0" vs "1.0.0") are considered equal
    /// while ones with a distinguishing suffix (e.g. "1.0" vs "1.0f") are not.
    /// </summary>
    private static bool ResiduesMatch(string local, Match localMatch, string online, Match onlineMatch) =>
        string.Equals(Residue(local, localMatch), Residue(online, onlineMatch), StringComparison.OrdinalIgnoreCase);

    private static string Residue(string value, Match match) =>
        (value[..match.Index] + value[(match.Index + match.Length)..]).Trim();

    /// <summary>
    /// Compares two numeric segment arrays. Returns &gt;0 when <paramref name="a"/>
    /// is greater, &lt;0 when smaller, 0 when equal. Missing trailing segments are
    /// treated as zero (so "1.2" == "1.2.0").
    /// </summary>
    private static int CompareSegments(int[] a, int[] b)
    {
        int length = Math.Max(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            int av = i < a.Length ? a[i] : 0;
            int bv = i < b.Length ? b[i] : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }

        return 0;
    }
}
