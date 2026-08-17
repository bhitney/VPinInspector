using System.Text;
using System.Text.RegularExpressions;

namespace VPin.Inspector.Vps;

/// <summary>
/// Fuzzy game-name matching between the PinUP Popper Games table and the VPS
/// puplookup.csv. Manufacturer and year are expected to be gated exactly by the
/// caller; this class only compares the descriptive name, which varies a lot in
/// practice, e.g.:
/// <list type="bullet">
/// <item>"Aces High (Bally 1965)" == "Aces High (Bally 1965)" (exact)</item>
/// <item>"AC-DC LUCI (Stern 2013) VPW" ~= "AC/DC (LUCI Premium) (Stern 2013)"</item>
/// </list>
/// The approach: strip any parenthesized groups (the (Manufacturer Year) and
/// edition/mod markers become extra tokens), normalize punctuation/casing, drop
/// noise tokens, and compare the resulting token sets with a containment-biased
/// overlap score.
/// </summary>
public static partial class PupNameMatcher
{
    [GeneratedRegex(@"\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex GroupRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumRegex();

    // Common edition / mod / build noise tokens that shouldn't drive matching.
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of",
        "premium", "pro", "le", "limited", "edition", "special", "deluxe",
        "vpw", "mod", "vr", "room", "hybrid", "fizx", "bam", "nfozzy",
        "pup", "puppack", "sg1bs",
    };

    /// <summary>
    /// The normalized token set for a raw game name: all parenthesized groups are
    /// unwrapped into tokens, punctuation is flattened, and noise tokens dropped.
    /// </summary>
    public static IReadOnlySet<string> Tokenize(string? name)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(name))
        {
            return tokens;
        }

        // Unwrap parenthesized groups: keep their inner text as candidate tokens.
        string flattened = GroupRegex().Replace(name, m => " " + m.Groups[1].Value + " ");
        flattened = flattened.ToLowerInvariant();

        foreach (string token in NonAlphaNumRegex().Split(flattened))
        {
            if (token.Length == 0 || NoiseTokens.Contains(token))
            {
                continue;
            }

            // Drop bare four-digit years (already gated separately).
            if (token.Length == 4 && token.All(char.IsDigit))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Scores name similarity in [0, 1]. 1.0 = one token set contains the other
    /// (which covers exact matches and the "AC/DC" vs "AC-DC LUCI" case), lower
    /// values reflect partial overlap (Jaccard). Empty sets score 0.
    /// </summary>
    public static double Score(string? left, string? right)
    {
        IReadOnlySet<string> a = Tokenize(left);
        IReadOnlySet<string> b = Tokenize(right);
        return Score(a, b);
    }

    /// <summary>Scores two already-tokenized names.</summary>
    public static double Score(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0d;
        }

        int intersection = a.Count(b.Contains);
        if (intersection == 0)
        {
            return 0d;
        }

        // Containment: if the smaller set is fully inside the larger, treat it as
        // a strong match (handles extra edition/mod tokens on one side).
        int smaller = Math.Min(a.Count, b.Count);
        if (intersection == smaller)
        {
            return 1d;
        }

        int union = a.Count + b.Count - intersection;
        return (double)intersection / union;
    }

    /// <summary>
    /// Convenience: true when <see cref="Score(string, string)"/> meets or exceeds
    /// <paramref name="threshold"/> (default 0.5).
    /// </summary>
    public static bool IsMatch(string? left, string? right, double threshold = 0.5) =>
        Score(left, right) >= threshold;
}
