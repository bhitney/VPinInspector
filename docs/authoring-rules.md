# Authoring Rules for VPX Inspector

This guide is written to be **AI-friendly**: paste it (or point an assistant at it)
and describe the check you want in plain English. The assistant can then produce a
correct `rules.json` entry without reading the whole codebase.

---

## TL;DR — describe your check, get a rule

Tell the assistant:

1. **What element(s)** to look at — by name pattern and/or element type.
2. **What condition** flags a problem — usually a timer interval comparison.
3. **What you'd suggest** as the corrected value (optional).

Example request:

> "Flag any Timer named like *pulse* whose interval is faster than 20ms, and suggest 50."

Produces:

```json
{
  "id": "fast-pulse-timer",
  "description": "Pulse timer running too frequently",
  "enabled": true,
  "namePatterns": [ "*pulse*" ],
  "types": [ "Timer" ],
  "mustBeTimer": true,
  "interval": "<20",
  "suggest": 50
}
```

Add that object to the `rules` array in [`rules.json`](../rules.json).

---

## Two families of checks

VPX Inspector has **two kinds of checks**, surfaced as separate groups in the UI
tree. Most of this document is about Deep Analysis rules (the ones you author in
JSON). Configuration checks are code-defined; you enable/configure them.

| | **Deep Analysis** | **Configuration** |
|---|---|---|
| Scope | One table at a time | The whole collection (folder + external state) |
| Reads | Inside the `.vpx` (elements, script) | Filesystem listing + external data (e.g. a database) |
| Defined by | Data — the `rules[]` array in rules.json | Code — a class implementing `IConfigurationCheck` |
| Honors excludes / time budget | Yes | Optional per check (`respectExcludePatterns`); ignores time budget |
| Runs on "Rescan flagged" | Yes | No (full scan only) |
| Config location | `rules[]` | `settings.configurationChecks` |

If you're writing a JSON rule, you want **Deep Analysis** (the rest of this doc).
Adding a new **Configuration** check is a code task — see
"Adding a configuration check" near the end.

---

## Project structure (what reads your rule)

```
Program.cs                     Entry point. No args -> GUI; path arg -> console scan.
rules.json                     Settings + configurationChecks + rules (the file you edit).
Vpx/
  VpxCompoundFile.cs           Opens .vpx (OLE2/MS-CFB), reads GameItem + GameData streams.
  BiffReader.cs                Parses BIFF records ([Int32 size][4-char tag][data]).
  GameItem.cs                  Parsed element: Name, TypeName, TimerEnabled, TimerIntervalMs.
  ScriptAnalyzer.cs            Extracts cGameName from the table script.
  Rules/
	InspectionRule.cs          Rule + settings schema (RuleSet, InspectionRule, InspectionSettings,
							   ConfigurationChecksSettings, PinupMatchSettings).
	IntervalCondition.cs       Parses "interval" strings (>=10, <10, >40, ==135, ...).
	RuleEngine.cs              Loads rules.json, compiles name globs, evaluates each element.
  Checks/                      Configuration (collection-scope) checks.
	IConfigurationCheck.cs     Contract + ConfigurationCheckContext (folder + exclude-aware listing).
	ConfigurationCheckResult.cs Generic result (SummaryLines + severity-tagged Sections).
	ConfigurationCheckRunner.cs Registry: builds + runs the enabled checks.
  Pinup/
	PinupDatabase.cs           Read-only SQLite access to PUPDatabase.db.
	PinupGameMatchCheck.cs     "pinup-game-match" configuration check.
  TableScanService.cs          Resolves .vpx files, applies excludes/time budget, runs the engine.
  TableResult.cs               Per-table outcome (matches, GameName, failure).
  ReportFormatter.cs           Renders console/UI report + summary + configuration checks.
UI/
  MainForm.cs                  WinForms window: folder picker, rules tree, settings, output.
  AppUi.cs                     STA message-loop host for the GUI.
```

**Data flow:** `TableScanService` opens each `.vpx` → `VpxCompoundFile` parses the
`GameItem` streams into `GameItem` objects → `RuleEngine.Evaluate` tests every
enabled rule against every element → matches roll up into `TableResult` →
`ReportFormatter` prints them.

---

## What a rule can inspect (the `GameItem` fields)

Each element exposes these to the matcher:

| Field             | Meaning                                                | Used by rule field |
|-------------------|--------------------------------------------------------|--------------------|
| `Name`            | Element name (e.g. `BallShadowUpdate`, `LeftSling`)    | `namePatterns`     |
| `TypeName`        | Element type (see list below)                          | `types`            |
| `TimerEnabled`    | Whether the element's timer is on (`TMON`)             | (reported only)    |
| `TimerIntervalMs` | Timer interval in ms (`TMIN`); `-1` = frame timer      | `interval`         |
| `HasTimer`        | True when `TimerIntervalMs >= 0`                        | `mustBeTimer`      |

### Valid element type names (for `types`)

`Surface`, `Flipper`, `Timer`, `Plunger`, `TextBox`, `Bumper`, `Trigger`,
`Light`, `Kicker`, `Decal`, `Gate`, `Spinner`, `Ramp`, `Table`, `LightCenter`,
`DragPoint`, `Collection`, `DispReel`, `LightSeq`, `Primitive`, `Flasher`,
`Rubber`, `HitTarget`.

> Tip: in Visual Pinball a "wall" is a `Surface`. Slingshots are usually
> `Surface` elements.

---

## Rule schema reference

```jsonc
{
  "id": "unique-id",             // required. stable, kebab-case identifier.
  "description": "why it matters",// shown in the report when matched.
  "enabled": true,               // optional (default true). false = skip in console;
								 //   in the UI it starts unchecked.
  "namePatterns": [ "*sling*" ], // optional. globs (* and ?), case-insensitive.
								 //   omit to match by type/interval alone.
  "types": [ "Surface" ],        // optional. element-type allow-list, case-insensitive.
								 //   omit to match any type.
  "mustBeTimer": true,           // optional. true = only elements with a timer.
  "interval": ">40",             // optional. comparison on TimerIntervalMs (ms).
								 //   omit to match regardless of interval.
  "suggest": 40                  // optional. recommended interval; shown as
								 //   "-> 40ms". -1 renders as "-1 (frame timer)".
								 //   Advisory only; never written to the table.
}
```

### Matching semantics (ALL provided conditions must pass — logical AND)

1. If `mustBeTimer` is true and the element has no timer → **no match**.
2. If `types` is set and the element's `TypeName` is not in it → **no match**.
3. If `namePatterns` is set and the name matches none of them → **no match**.
4. If `interval` is set:
   - element must have a timer (`HasTimer`), else **no match**;
   - `TimerIntervalMs` must satisfy the comparison, else **no match**.
5. Otherwise → **match**.

An empty/omitted field means "don't filter on this."

### `interval` operators

`>`, `>=`, `<`, `<=`, `==`, `!=`, or a bare number (means `==`).
Examples: `">=10"`, `"<10"`, `">40"`, `"==135"`, `"100"`.

> Note: `TimerIntervalMs == -1` means "frame timer" and counts as **no timer**
> for matching (`HasTimer` is false), so an already-frame-timed element won't be
> flagged by an `interval` rule. Use `suggest: -1` to *recommend* a frame timer.

---

## Recipes (common request → rule)

**"Only look at real slingshots (walls), warn if slower than 40ms, suggest 40."**
```json
{ "id": "slingshot", "description": "Slingshot timer interval",
  "namePatterns": [ "*sling*" ], "types": [ "Surface" ],
  "mustBeTimer": true, "interval": ">40", "suggest": 40 }
```

**"Any timer firing faster than every 10ms, regardless of name."**
```json
{ "id": "hot-timer", "description": "Very high-frequency timer",
  "types": [ "Timer" ], "mustBeTimer": true, "interval": "<10" }
```

**"Ball shadow timers of any interval; recommend a frame timer (-1)."**
```json
{ "id": "ball-shadow", "description": "Ball shadow update timer is misconfigured",
  "namePatterns": [ "BallShadowUpdate", "*shadow*" ], "types": [ "Timer" ],
  "mustBeTimer": true, "interval": ">=10", "suggest": -1 }
```

**"List every element named like *GI* (no interval check)."**
```json
{ "id": "gi-audit", "description": "General illumination elements",
  "namePatterns": [ "*gi*", "gi_*" ] }
```

---

## Settings (top of `rules.json`, not a rule)

```json
"settings": {
  "maxRunTimeSeconds": 0,           // 0 = no limit; >0 stops the scan after N seconds.
  "excludePatterns": [ "VR ROOM*" ],// file-name globs to skip (case-insensitive).
  "configurationChecks": {          // collection-scope checks (see below).
    "pinup-game-match": {
      "enabled": true,
      "respectExcludePatterns": false,
      "databasePath": "C:\\vPinball\\PinUPSystem\\PUPDatabase.db",
      "emulatorIds": [],
      "matchEmulatorsByFolder": true,
      "visibleOnly": false
    }
  }
}
```

`maxRunTimeSeconds` and `excludePatterns` can also be edited live in the GUI's
**Settings** panel (overrides the file for that run without saving).

---

## Configuration checks

Configuration checks compare the collection (the tables folder) against external
state. They appear under the **Configuration** group in the UI tree and run only
on a **full folder scan**.

Every configuration check shares two settings:

| Field | Meaning |
|---|---|
| `enabled` | Whether the check runs (also reflected by its checkbox). |
| `respectExcludePatterns` | `false` (default) = compare against the raw filesystem; `true` = skip files matching the active exclude globs. |

> Why `respectExcludePatterns` defaults to false: a database-vs-disk comparison
> usually wants the *real* files. But excluding is a legitimate choice — e.g. if
> you don't care about flagging `VR ROOM*` variants, set it true.

### `pinup-game-match`

Compares `.vpx` files in the folder against games in the PinUP Popper database.

| Field | Meaning |
|---|---|
| `databasePath` | Path to `PUPDatabase.db`. |
| `emulatorIds` | Explicit emulator IDs (EMUID) to include, e.g. `[1, 7, 10]`. |
| `matchEmulatorsByFolder` | Also include emulators whose `DirGames` equals the scanned folder (normalized). |
| `visibleOnly` | Restrict to Visible emulators/games. |

Reports **[ERROR]** for games in the DB but missing on disk, and **[INFO]** for
files on disk not in the DB.

### Adding a configuration check (code task)

1. Create a settings class extending `ConfigurationCheckSettings` (add its own
   fields) and register it in `ConfigurationChecksSettings` with a JSON key.
2. Create a class implementing `IConfigurationCheck` (`Id`, `Description`,
   `Enabled`, `Run(context)`), returning a `ConfigurationCheckResult` with
   `SummaryLines` and severity-tagged `CheckSection`s.
3. Register it in `ConfigurationCheckRunner.BuildChecks`.
4. Add its config block under `settings.configurationChecks` in rules.json.

`ConfigurationCheckContext` gives you `ResolveFolder()` and
`EnumerateVpxFileNames(folder, respectExcludePatterns)` so the exclude choice is
honored consistently.

---

## Checklist before adding a rule

- [ ] `id` is unique and kebab-case.
- [ ] `description` explains the "why" (it appears in reports).
- [ ] Used `types` to avoid false positives (e.g. `Surface` vs `Rubber` vs `Flasher`).
- [ ] `interval` operator + number is quoted (it's a string).
- [ ] JSON is valid — object added inside the `rules` array, comma-separated.
- [ ] In the GUI, click **Reload rules**, then **Scan** to apply.

---

## How to validate a new rule

- **GUI:** click **Edit rules.json** → add your entry → **Reload rules** →
  **Scan** (or **Rescan flagged**).
- **Console:** `VPX Inspector "<folder-or-file>"` and inspect the summary checklist.
- A rule that matches nothing is usually a `types` mismatch (check the element's
  `TypeName` in the detailed per-table output) or an over-narrow `namePatterns`.
