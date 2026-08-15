using VPin.Inspector.Vpx.Pinup;

namespace VPin.Inspector.Vps;

/// <summary>How a Popper game was matched to VPS puplookup.csv rows.</summary>
public enum PupMatchKind
{
    /// <summary>No match found.</summary>
    None,

    /// <summary>Matched definitively by VPS WEBGameID.</summary>
    WebGameId,

    /// <summary>Matched by exact manufacturer/year plus fuzzy game name.</summary>
    Fuzzy,
}

/// <summary>
/// The result of matching one Popper game against the VPS reference data.
/// </summary>
public sealed record PupMatchResult(
    PupMatchKind Kind,
    IReadOnlyList<PupLookupRow> Rows,
    string? ClosestName,
    double ClosestScore)
{
    public bool Matched => Kind != PupMatchKind.None;

    /// <summary>
    /// True when the game's manufacturer/year had no candidates at all in the CSV
    /// (as opposed to having candidates that simply didn't fuzzy-match).
    /// </summary>
    public bool NoCandidates { get; init; }

    public static readonly PupMatchResult NoManufacturerYear =
        new(PupMatchKind.None, Array.Empty<PupLookupRow>(), null, 0d) { NoCandidates = true };
}

/// <summary>
/// Indexes VPS puplookup.csv rows for matching against PinUP Popper games and
/// centralizes the match policy so every rule matches identically:
/// <list type="number">
/// <item>If the game has a VPS WEBGameID and the CSV has row(s) with that id,
/// those rows are the definitive match (no fuzzy guessing). This is the
/// preferred key; it's sparse today but can be populated over time.</item>
/// <item>Otherwise fall back to exact manufacturer/year gating plus fuzzy game
/// name matching (the historical behavior).</item>
/// </list>
/// </summary>
public sealed class PupLookupIndex
{
    private readonly Dictionary<string, List<PupLookupRow>> _byWebGameId;
    private readonly Dictionary<string, List<PupLookupRow>> _byManufacturerYear;

    private PupLookupIndex(
        Dictionary<string, List<PupLookupRow>> byWebGameId,
        Dictionary<string, List<PupLookupRow>> byManufacturerYear)
    {
        _byWebGameId = byWebGameId;
        _byManufacturerYear = byManufacturerYear;
    }

    /// <summary>The CSV column holding the VPS web game id.</summary>
    public const string WebGameIdColumn = "WEBGameID";

    /// <summary>Builds the index from a loaded lookup table.</summary>
    public static PupLookupIndex Build(PupLookupTable lookup)
    {
        var byWebGameId = new Dictionary<string, List<PupLookupRow>>(StringComparer.OrdinalIgnoreCase);
        var byManufacturerYear = new Dictionary<string, List<PupLookupRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (PupLookupRow row in lookup.Rows)
        {
            string? webId = row.Get(WebGameIdColumn);
            if (!string.IsNullOrWhiteSpace(webId))
            {
                Add(byWebGameId, webId.Trim(), row);
            }

            string key = MakeManufacturerYearKey(row.Get("Manufact"), row.Get("GameYear"));
            if (key.Length != 0)
            {
                Add(byManufacturerYear, key, row);
            }
        }

        return new PupLookupIndex(byWebGameId, byManufacturerYear);
    }

    /// <summary>
    /// Matches a Popper game to VPS rows using the WEBGameID key first, then the
    /// manufacturer/year + fuzzy-name fallback.
    /// </summary>
    public PupMatchResult Match(PinupGameIdentity game, double fuzzyThreshold)
    {
        // 1. Definitive match by VPS WEBGameID when both sides have it.
        if (!string.IsNullOrWhiteSpace(game.WebGameId) &&
            _byWebGameId.TryGetValue(game.WebGameId.Trim(), out List<PupLookupRow>? byId))
        {
            return new PupMatchResult(PupMatchKind.WebGameId, byId, byId[0].Get("GameName"), 1d);
        }

        // 2. Fallback: exact manufacturer/year gate, then fuzzy name.
        string mykey = MakeManufacturerYearKey(game.Manufacturer, game.Year);
        if (mykey.Length == 0 || !_byManufacturerYear.TryGetValue(mykey, out List<PupLookupRow>? candidates))
        {
            return PupMatchResult.NoManufacturerYear;
        }

        var matched = new List<PupLookupRow>();
        double bestScore = 0d;
        string? bestName = null;

        foreach (PupLookupRow candidate in candidates)
        {
            double score = PupNameMatcher.Score(game.GameName, candidate.Get("GameName"));
            if (score > bestScore)
            {
                bestScore = score;
                bestName = candidate.Get("GameName");
            }

            if (score >= fuzzyThreshold)
            {
                matched.Add(candidate);
            }
        }

        return matched.Count > 0
            ? new PupMatchResult(PupMatchKind.Fuzzy, matched, bestName, bestScore)
            : new PupMatchResult(PupMatchKind.None, Array.Empty<PupLookupRow>(), bestName, bestScore);
    }

    private static string MakeManufacturerYearKey(string? manufacturer, string? year)
    {
        string m = manufacturer?.Trim() ?? string.Empty;
        string y = year?.Trim() ?? string.Empty;
        return m.Length == 0 || y.Length == 0 ? string.Empty : $"{m}|{y}";
    }

    private static void Add(Dictionary<string, List<PupLookupRow>> map, string key, PupLookupRow row)
    {
        if (!map.TryGetValue(key, out List<PupLookupRow>? bucket))
        {
            bucket = new List<PupLookupRow>();
            map[key] = bucket;
        }

        bucket.Add(row);
    }
}
