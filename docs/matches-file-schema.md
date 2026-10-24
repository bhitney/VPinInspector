# matches.csv schema

`matches.csv` pre-seeds the VPS Match Assistant ("Find Matches By File"). Each row
maps one local PinUP Popper game to a VPS `WEBGameID`. The app does **no vetting**:
whatever is in the file is pre-selected, then reviewed by the user.

## Format

- UTF-8 CSV with a header row; the header names are matched case-insensitively.
- Fields containing commas, quotes, or newlines must be double-quoted (`""` escapes a quote).
- One row per local game. If a `GameID` appears twice, the last row wins.

## Columns

| Column | Required | Used by app | Description |
| --- | --- | --- | --- |
| `GameID` | Yes | Yes | Primary key of the game in the PUPDatabase `Games` table (integer). Rows are joined to local games on this value. |
| `MatchedWebGameID` | Yes | Yes | The VPS `WEBGameID` from `puplookup.csv` to select. Leave empty when there is no match; the row is ignored. |
| `Confidence` | Recommended | Yes | Free-text label shown in the Confidence column. Recommended values: `High`, `Medium`, `Low`, `None`. |
| `Reason` | Optional | Loaded, not yet displayed | Short justification for the match. |
| `GameName` | Optional | No | Local game name (aids human review). |
| `GameFileName` | Optional | No | Local table file name. |
| `Author` | Optional | No | Local author list. |
| `GAMEVER` | Optional | No | Local version. |
| `MatchedLookupGameFileName` | Optional | No | `GameFileName` of the matched `puplookup.csv` row. |
| `MatchedLookupAuthor` | Optional | No | `Author` of the matched row. |
| `MatchedLookupVersion` | Optional | No | `GAMEVER` of the matched row. |

Extra columns are ignored. Column order does not matter.

## Header (reference)

```
GameID,GameName,GameFileName,Author,GAMEVER,MatchedWebGameID,MatchedLookupGameFileName,MatchedLookupAuthor,MatchedLookupVersion,Confidence,Reason
```

## Example

```csv
GameID,GameName,GameFileName,Author,GAMEVER,MatchedWebGameID,MatchedLookupGameFileName,MatchedLookupAuthor,MatchedLookupVersion,Confidence,Reason
144,Dark Crystal Pinball (Original 2020),Dark Crystal Pinball (Original 2020).vpx,"Wpilot, JPSalas",1.2,3W46guJa,"Dark Crystal Pinball, The (Original 2020) wpilot 1.2 MOD","wpilot, JPSalas",1.2,High,Title exact; author matches; version 1.2 exact.
```

## Guidance for generating a file

- `MatchedWebGameID` must exist in `puplookup.csv`'s `WEBGameID` column. If it is
  not among the app's fuzzy candidates, the app adds it to the candidate list so
  it can still be reviewed.
- Emit a row for every game you evaluated, using an empty `MatchedWebGameID` and
  `Confidence` of `None` for no match, so reviewers can see what was considered.
- Use `Confidence` honestly; it is the user's main cue for how closely to review.

Parser: `MatchEngine.LoadMatchFile` in `VpsMatchAssistant/MatchEngine.cs`.
