using VPin.Inspector.Vps;
using VPin.Inspector.Vpx.Pinup;

namespace VPin.MatchAssistant;

/// <summary>One VPS puplookup.csv candidate presented to the user for a game.</summary>
public sealed record VpsCandidate(
    string WebGameId,
    string GameName,
    string? Manufacturer,
    string? Year,
    string? Version,
    string? Author,
    double Score);

/// <summary>
/// A local Popper game (empty WEBGameID) with its ranked VPS candidates and the
/// auto-suggested selection.
/// </summary>
public sealed class GameMatch
{
    public required long GameKey { get; init; }
    public required PinupGameIdentity Game { get; init; }
    public required IReadOnlyList<VpsCandidate> Candidates { get; init; }
    public required PupMatchKind Kind { get; init; }

    /// <summary>The WEBGameID the user has chosen to write (null = skip/undecided).</summary>
    public string? SelectedWebGameId { get; set; }

    /// <summary>True when a single high-confidence candidate can be auto-suggested.</summary>
    public bool HasConfidentSuggestion { get; init; }

    /// <summary>Confidence label from the matches file (null when not seeded from a file).</summary>
    public string? Confidence { get; init; }

    /// <summary>Reason text from the matches file.</summary>
    public string? Reason { get; init; }

    /// <summary>True when the selection was seeded from the matches file.</summary>
    public bool FromFile { get; init; }
}

/// <summary>One pre-computed match loaded from matches.csv.</summary>
public sealed record FileMatch(string WebGameId, string? Confidence, string? Reason);

/// <summary>
/// Drives the matching workflow for the companion app, reusing the shared
/// <see cref="PupLookupIndex"/> policy and VPS reference data.
/// </summary>
public sealed class MatchEngine
{
    /// <summary>Fuzzy threshold used to gate the candidate list (same as the hygiene rule).</summary>
    public const double MatchThreshold = 0.5;

    /// <summary>Score at/above which a single unique candidate is auto-suggested.</summary>
    public const double ConfidentThreshold = 0.85;

    private static readonly IReadOnlyList<string> LookupColumns = new[]
    {
        "GameName", "Manufact", "GameYear", "GAMEVER", "WEBGameID",
        "GameFileName", "Author", "VPS-ID", "MasterID",
    };

    private readonly PupLookupIndex _index;
    private readonly PupLookupTable _lookup;

    private MatchEngine(PupLookupIndex index, PupLookupTable lookup)
    {
        _index = index;
        _lookup = lookup;
    }

    /// <summary>Loads the CSV and builds the match index.</summary>
    public static MatchEngine Load(string csvPath)
    {
        PupLookupTable lookup = PupLookupTable.Load(csvPath, LookupColumns);
        return new MatchEngine(PupLookupIndex.Build(lookup), lookup);
    }

    /// <summary>
    /// Loads matches.csv (GameID, MatchedWebGameID, Confidence, Reason) keyed by GameID.
    /// Rows without a MatchedWebGameID are ignored.
    /// </summary>
    public static IReadOnlyDictionary<long, FileMatch> LoadMatchFile(string path)
    {
        PupLookupTable table = PupLookupTable.Load(
            path, new[] { "GameID", "MatchedWebGameID", "Confidence", "Reason" });

        var map = new Dictionary<long, FileMatch>();
        foreach (PupLookupRow r in table.Rows)
        {
            string? web = r.Get("MatchedWebGameID");
            if (!long.TryParse(r.Get("GameID"), out long id) || string.IsNullOrWhiteSpace(web))
            {
                continue;
            }

            map[id] = new FileMatch(web, r.Get("Confidence"), r.Get("Reason"));
        }

        return map;
    }

    private VpsCandidate? FindCandidate(string webGameId, string localName)
    {
        PupLookupRow? r = _lookup.Rows.FirstOrDefault(x =>
            string.Equals(x.Get(PupLookupIndex.WebGameIdColumn), webGameId, StringComparison.OrdinalIgnoreCase));
        return r is null ? null : new VpsCandidate(
            WebGameId: webGameId,
            GameName: r.Get("GameName") ?? string.Empty,
            Manufacturer: r.Get("Manufact"),
            Year: r.Get("GameYear"),
            Version: r.Get("GAMEVER"),
            Author: r.Get("Author"),
            Score: PupNameMatcher.Score(localName, r.Get("GameName")));
    }

    /// <summary>
    /// Free-text search across
    /// insensitive substring) against GameName, Manufact, GameYear and Author.
    /// All whitespace-separated tokens must be found in at least one field.
    /// </summary>
    public IReadOnlyList<VpsCandidate> SearchAll(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<VpsCandidate>();
        }

        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Array.Empty<VpsCandidate>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<VpsCandidate>();

        foreach (PupLookupRow r in _lookup.Rows)
        {
            string web = r.Get(PupLookupIndex.WebGameIdColumn) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(web) || !seen.Add(web))
            {
                continue;
            }

            string haystack = string.Join(
                '\u0001',
                r.Get("GameName") ?? string.Empty,
                r.Get("Manufact") ?? string.Empty,
                r.Get("GameYear") ?? string.Empty,
                r.Get("Author") ?? string.Empty);

            bool allMatch = tokens.All(t =>
                haystack.Contains(t, StringComparison.OrdinalIgnoreCase));

            if (!allMatch)
            {
                continue;
            }

            results.Add(new VpsCandidate(
                WebGameId: web,
                GameName: r.Get("GameName") ?? string.Empty,
                Manufacturer: r.Get("Manufact"),
                Year: r.Get("GameYear"),
                Version: r.Get("GAMEVER"),
                Author: r.Get("Author"),
                Score: 0.0));

            if (results.Count >= 500)
            {
                break;
            }
        }

        return results
            .OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds match rows for the given emulator's games. By default only games
    /// with an empty WEBGameID are returned; set <paramref name="includePopulated"/>
    /// to also include games that already have a WEBGameID.
    /// </summary>
    public IReadOnlyList<GameMatch> BuildMatches(
        PinupDatabase db, int emulatorId, bool visibleOnly, bool includePopulated,
        IReadOnlyDictionary<long, FileMatch>? fileMatches = null)
    {
        IReadOnlyList<PinupGameIdentityKeyed> games =
            db.GetGameIdentitiesWithKey(new[] { emulatorId });

        var results = new List<GameMatch>();

        foreach (PinupGameIdentityKeyed keyed in games)
        {
            PinupGameIdentity game = keyed.Identity;

            if (visibleOnly && !game.Visible)
            {
                continue;
            }

            bool alreadyPopulated = !string.IsNullOrWhiteSpace(game.WebGameId);
            if (alreadyPopulated && !includePopulated)
            {
                continue; // already populated
            }

            if (string.IsNullOrWhiteSpace(game.GameName))
            {
                continue;
            }

            PupMatchResult match = _index.Match(game, MatchThreshold);

            var candidates = match.Rows
                .Select(r => new VpsCandidate(
                    WebGameId: r.Get(PupLookupIndex.WebGameIdColumn) ?? string.Empty,
                    GameName: r.Get("GameName") ?? string.Empty,
                    Manufacturer: r.Get("Manufact"),
                    Year: r.Get("GameYear"),
                    Version: r.Get("GAMEVER"),
                    Author: r.Get("Author"),
                    Score: PupNameMatcher.Score(game.GameName, r.Get("GameName"))))
                .Where(c => !string.IsNullOrWhiteSpace(c.WebGameId))
                .OrderByDescending(c => c.Score)
                .ToList();

            bool confident =
                candidates.Count == 1 && candidates[0].Score >= MatchThreshold ||
                candidates.Count > 0 && candidates[0].Score >= ConfidentThreshold &&
                    (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= 0.15);

            if (fileMatches is not null)
            {
                // The file is authoritative: no vetting, no auto-suggestion of our own.
                fileMatches.TryGetValue(keyed.GameKey, out FileMatch? fm);
                if (fm is not null && !candidates.Any(c =>
                        string.Equals(c.WebGameId, fm.WebGameId, StringComparison.OrdinalIgnoreCase)))
                {
                    VpsCandidate? extra = FindCandidate(fm.WebGameId, game.GameName);
                    if (extra is not null)
                    {
                        candidates.Insert(0, extra);
                    }
                }

                results.Add(new GameMatch
                {
                    GameKey = keyed.GameKey,
                    Game = game,
                    Candidates = candidates,
                    Kind = match.Kind,
                    HasConfidentSuggestion = false,
                    SelectedWebGameId = fm?.WebGameId,
                    Confidence = fm?.Confidence,
                    Reason = fm?.Reason,
                    FromFile = fm is not null,
                });
                continue;
            }

            results.Add(new GameMatch
            {
                GameKey = keyed.GameKey,
                Game = game,
                Candidates = candidates,
                Kind = match.Kind,
                HasConfidentSuggestion = confident,
                SelectedWebGameId = confident ? candidates[0].WebGameId : null,
            });
        }

        return results;
    }
}
