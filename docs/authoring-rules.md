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
Core/                          Platform-neutral inspection pipeline.
  Model/                       TableElement (base), ITimerElement, PinballTable (base).
  Platforms/IPinballPlatform.cs  Seam a new emulator implements.
  Rules/                       Finding, ITableRule, ICollectionRule, TableContext, CollectionContext.
  Reporting/ScanReport.cs      ScanReport + TableReport (per-table severity) + CollectionFindingGroup.
  InspectionRegistry.cs        Central registration of platforms + rules.
  InspectionService.cs         Resolves files, streams per-table results, produces a ScanReport.
Platforms/Vpx/                 VPX platform adapter (one of potentially many).
  Model/VpxGameItem.cs         VPX element (: TableElement, ITimerElement) + VpxItemType. Parses NAME/TMON/TMIN.
  Model/VpxTable.cs            VPX table document (: PinballTable).
  VpxPlatform.cs               Opens .vpx, produces a neutral VpxTable.
  VpxRegistryFactory.cs        Wires the platform, rules.json rules, and opt-in modules.
  Rules/                       DeclarativeElementRule (wraps rules.json), Dof/Pinup/VrRoom/DuplicateGameName rules.
  Reporting/ReportRenderer.cs  Renders a ScanReport to text / severity-tagged lines.
Vpx/
  VpxCompoundFile.cs           Opens .vpx (OLE2/MS-CFB), extracts the table script.
  BiffReader.cs                Parses BIFF records ([Int32 size][4-char tag][data]).
  ScriptAnalyzer.cs            Extracts cGameName from the table script.
  Rules/
	InspectionRule.cs          Rule + settings schema (RuleSet, InspectionRule, InspectionSettings,
							   ConfigurationChecksSettings, PinupMatchSettings).
	IntervalCondition.cs       Parses "interval" strings (>=10, <10, >40, ==135, ...).
	RuleEngine.cs              Loads rules.json (rules + settings). Loader only.
  Pinup/
	PinupDatabase.cs           Read-only SQLite access to PUPDatabase.db.
UI/
  MainForm.cs                  WinForms window: folder picker, rules tree, settings, log + summary panes.
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
  ,"severity": "warning"         // optional. "info" | "warning" | "error"
								 //   (case-insensitive). Default "warning".
								 //   Drives the color in the summary pane.
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

### `media-match`

Verifies that PinUP Popper media exists for each game registered for the selected
emulators. For every game, Popper looks in the emulator's `DirMedia` (falling back
to the `GlobalSettings` `GlobalMediaDir` when the emulator has none), inside a
per-type subfolder, for a file named after the game file with **any** extension
(the media may be a video or an image, e.g. `MyTable.mp4` or `MyTable.png`).

Each media folder is checked independently, so you can target just one media type
(e.g. only flag missing toppers) by listing only that folder.

| Field | Meaning |
|---|---|
| `databasePath` | Path to `PUPDatabase.db`. |
| `emulatorIds` | Explicit emulator IDs (EMUID) to include, e.g. `[1, 7, 10]`. When supplied, these take precedence and `matchEmulatorsByFolder` is ignored — letting you audit media for any games in the database regardless of where their `.vpx` files sit. |
| `matchEmulatorsByFolder` | Used only when `emulatorIds` is empty: include emulators whose `DirGames` equals the scanned folder (normalized). |
| `visibleOnly` | Restrict to Visible emulators/games. |
| `mediaFolders` | Media subfolders to check, e.g. `[ "Playfield", "Topper", "BackGlass", "DMD", "Loading", "Menu" ]`. Defaults to `Playfield`. |

Reports **[ERROR]** for each game with no media file in a selected folder, grouped
by media type.

### `vr-room-matching`

Checks that every `VR ROOM <name>.vpx` file has a matching non-VR `<name>.vpx` in
the same folder. By convention a VR ROOM file is the VR variant of an existing
table; a VR ROOM without its base table usually means the table is VR-only, which
is unusual.

| Field | Meaning |
|---|---|
| `prefix` | The VR ROOM filename prefix. Defaults to `"VR ROOM "`. |

Reports **[WARN]** for each VR ROOM file with no matching base table.

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
