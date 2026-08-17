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
}

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

    private MatchEngine(PupLookupIndex index) => _index = index;

    /// <summary>Loads the CSV and builds the match index.</summary>
    public static MatchEngine Load(string csvPath)
    {
        PupLookupTable lookup = PupLookupTable.Load(csvPath, LookupColumns);
        return new MatchEngine(PupLookupIndex.Build(lookup));
    }

    /// <summary>
    /// Builds match rows for the given emulator's games. By default only games
    /// with an empty WEBGameID are returned; set <paramref name="includePopulated"/>
    /// to also include games that already have a WEBGameID.
    /// </summary>
    public IReadOnlyList<GameMatch> BuildMatches(
        PinupDatabase db, int emulatorId, bool visibleOnly, bool includePopulated)
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
