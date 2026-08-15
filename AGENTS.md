# AGENTS.md — VPin Inspector

Context and working agreement for AI agent sessions on this repository. Read this
first before planning or editing.

## What this app is

VPin Inspector is a Windows tool that inspects Virtual Pinball tables (currently
Visual Pinball X `.vpx` files) and related configuration (PinUP Popper database,
DirectOutput/DOF config) and reports issues. It has two front-ends over a shared,
platform-neutral core:

- **Console** (`Program.cs`) — `VPinInspector <path> [rules.json]` scans a file or folder.
- **WinForms GUI** (`UI/`) — launched when run with no args (Windows only).

The product was formerly "VPX Inspector"; it was renamed to **VPin Inspector**
(namespace `VPin.Inspector.*`) to reflect that it is intended to grow beyond VPX
(e.g. Future Pinball) and beyond a single data source (e.g. the Virtual Pinball
Spreadsheet / VPS).

## Solution layout

Two projects (SDK-style), one solution `VPinInspector.slnx`:

| Project | TFM | Purpose |
| --- | --- | --- |
| `Core/VPinInspector.Core.csproj` | `net10.0` | Platform-neutral inspection pipeline. **No** WinForms, **no** external packages. |
| `VPinInspector.csproj` | `net10.0-windows` | The app: console + WinForms front-ends, the VPX platform adapter, and legacy VPX helpers. References Core. |

> The on-disk repo folder is still named `VPX Inspector` (to avoid disrupting the
> git path). Do **not** rename the folder. The project/solution files are
> `VPinInspector.csproj` / `VPinInspector.slnx`.

### Source map

```
Core/                              VPinInspector.Core (net10.0, neutral)
  Model/
	TableElement.cs                Abstract base element (Id, Name, TypeName, Properties bag). THIN.
	ITimerElement.cs               Capability interface (timers are opt-in, not base fields).
	PinballTable.cs                Abstract table document (Elements, GameName, Script). Parses TableName -> NameInfo.
	TableNameInfo.cs               TableNameInfo record + TableNameParser ("Name (Manufacturer Year) [PUP]").
  Platforms/
	IPinballPlatform.cs            Seam a new emulator implements (Id, FileExtensions, CanHandle, Load, LoadShallow).
  Rules/
	Finding.cs                     Neutral result DTO + FindingSeverity { Info, Warning, Error }.
	ITableRule.cs                  Per-table rule + TableContext + IInspectionRule (SupportedPlatforms, AnalysisDepth).
	ICollectionRule.cs             Collection-scope rule + CollectionContext (folder + file helpers).
  Reporting/
	ScanReport.cs                  ScanReport, TableReport (per-table Severity), CollectionFindingGroup.
  InspectionRegistry.cs            Central registration of platforms + table rules + collection rules.
  InspectionService.cs            Resolves files, streams per-table results, produces a ScanReport.
								   Owns ScanOptions (rule selection, excludes, time budget, explicit files).

Platforms/Vpx/                     VPX platform adapter (lives in the app project for now)
  Model/
	VpxGameItem.cs                 VPX element (: TableElement, ITimerElement) + VpxItemType enum. Parses NAME/TMON/TMIN.
	VpxTable.cs                    VPX table document (: PinballTable).
  VpxPlatform.cs                   Opens .vpx (OpenMcdf), builds a neutral VpxTable. LoadShallow = metadata only.
  VpxRegistryFactory.cs            Composition root: wires platform + rules.json rules + opt-in modules.
  Rules/
	DeclarativeElementRule.cs      Wraps ONE rules.json rule as an ITableRule (name/type/interval + severity). Deep.
	WellFormedNameRule.cs          ITableRule (Quick): flags names not matching "Name (Manufacturer Year)".
	BallShadowDepthMaskRule.cs     ITableRule (Deep): flags *shadow* primitives with "Hide parts behind" (ZMSK) checked.
	DofLookupRule.cs               Opt-in ICollectionRule (Deep): table ROM vs DOF config.
	PinupGameMatchRule.cs          Opt-in ICollectionRule (Quick): folder vs PinUP DB games.
	PinupMetadataCheckRule.cs      Opt-in ICollectionRule (Deep): ROM/manufacturer/year/version vs PinUP DB.
	PinupMediaMatchRule.cs         Opt-in ICollectionRule (Quick): PinUP media presence per media folder.
	VrRoomMatchRule.cs             Opt-in ICollectionRule (Quick): "VR ROOM x" has a base "x".
	DuplicateGameNameRule.cs       ICollectionRule (Deep): 2+ tables sharing a cGameName.
	PupHygieneRule.cs              Opt-in ICollectionRule (Quick): Popper Games vs VPS puplookup.csv (exact manufacturer/year + fuzzy name).
	VersionCheckRule.cs           Opt-in ICollectionRule (Quick): local Popper GAMEVER vs newest VPS puplookup.csv version.
	FastTimerRule.cs               Example ITableRule (NOT registered; illustration only).
  Reporting/
	RenderedLine.cs                Severity-tagged line (single source of truth for summary content).
	ReportRenderer.cs              ScanReport -> text (console) / RenderedLine[] (UI colors).

Vpx/                               Legacy VPX helpers (still used by the adapter)
  VpxCompoundFile.cs               Opens .vpx: GetScript (VBScript) + GetTableInfo (embedded TableName/Author/Version).
  BiffReader.cs                    Parses BIFF records ([Int32 size][4-char tag][data]).
  ScriptAnalyzer.cs                Extracts cGameName from the table script.
  Dof/DofConfig.cs                 Parses DOF .ini; HasRom(...) with base/prefix fallbacks.
  Pinup/PinupDatabase.cs           Read-only SQLite access to PUPDatabase.db (games, media, metadata).
  Rules/
	InspectionRule.cs              rules.json schema (RuleSet, InspectionRule, InspectionSettings, *Settings).
	IntervalCondition.cs           Parses "interval" strings (>=10, <10, >40, ==135, ...).
	RuleEngine.cs                  LOADER ONLY: reads rules.json into rules + settings.

Program.cs                         Entry point. No args -> GUI; path arg -> console scan.
UI/AppUi.cs                        STA message-loop host for the GUI.
UI/MainForm.cs                     WinForms window: menu bar (File/Tools), folder picker, rules tree, settings, log + summary panes.
Vps/VpsDownloader.cs               Downloads VPS reference data (puplookup.csv, vpsdb.json) into the app directory.
Vps/PupLookupTable.cs              Quote-aware CSV loader for puplookup.csv (configurable columns).
Vps/PupNameMatcher.cs              Fuzzy game-name matcher (token normalization + containment/Jaccard score).
Vps/PupLookupIndex.cs              Popper-game→VPS matcher: WEBGameID-first, then manufacturer/year + fuzzy-name fallback.
Vps/VersionComparer.cs             Heuristic version ordering (Equal/OnlineNewer/LocalNewer/Unknown).
rules.json                         User-editable settings + configurationChecks + rules. Copied to output.
docs/authoring-rules.md            End-user guide for authoring rules.json.
```

## Architecture: the core model

The design goal is **modularity without rework**: new rules are self-contained
modules, but shared parsing work is done once and handed to every rule.

- **A rule declares what it needs and reads cached artifacts; it never opens files itself.**
  `InspectionService` loads each table once via its `IPinballPlatform`, wraps it in a
  `TableContext`, and passes the same context to every table rule. Collection rules
  receive all loaded tables via `CollectionContext`.
- **Two rule scopes:**
  - `ITableRule.Evaluate(TableContext)` — runs per table.
  - `ICollectionRule.Evaluate(CollectionContext)` — runs once over the whole scan.
- **Analysis depth (cost), independent of scope:** every rule declares an
  `AnalysisDepth` — `Quick` (reads only file names / folder listings / external
  databases) or `Deep` (must fully parse each `.vpx`: its elements and/or the
  script for `cGameName`). This is orthogonal to scope: a collection rule can be
  Deep (`duplicate-game-name`, `dof-lookup` need `cGameName`) and a table rule can
  be Quick (`well-formed-name` only reads the file name). The UI groups rules as
  **Quick Checks** vs **Deep Analysis** by this flag, not by scope.
- **Shallow load optimization:** `InspectionService` computes whether any selected
  rule is `Deep`. If none are, it loads each table via
  `IPinballPlatform.LoadShallow` (metadata-only `PinballTable`: file path/name, no
  elements, no script) so a quick-only scan skips the expensive BIFF/script parse.
  `LoadShallow` defaults to `Load`; platforms with a slow parse override it
  (`VpxPlatform` does).
- **Platform neutrality:** rules operate on `TableElement` / `PinballTable` and
  **capability interfaces** (e.g. `ITimerElement`), never on VPX-specific types.
  Timers/positions/etc. are interfaces so a future platform that lacks them simply
  doesn't implement them.
- **Platform gate:** a rule's `SupportedPlatforms` set filters it by platform id
  (empty set = platform-agnostic, runs everywhere).
- **Opt-in everything:** each rule has `EnabledByDefault`; per-run selection is via
  `ScanOptions.SelectedRuleIds`. Nobody is forced to use DOF/PinUP/etc.
- **Severity is standardized end-to-end:** every `Finding` has a `FindingSeverity`
  ({Info, Warning, Error}); `TableReport.Severity` is the max of its findings (or
  Error if unreadable, Info if clean). The UI colors only the `[TAG]` span.

### Data flow

```
InspectionService.Scan(inputPath, options, onTable, ct)
  -> ResolveFiles                       (platform extensions + excludes, or explicit files)
  -> RequiresDeepAnalysis(options)?     (any selected rule Deep -> full parse, else shallow)
  -> for each file: platform.Load /     (parse once -> PinballTable)
       platform.LoadShallow             (metadata-only when no Deep rule selected)
	   -> TableContext                  (shared by all table rules)
	   -> run selected ITableRules      (platform-gated) -> Finding[]
	   -> TableReport (+ onTable stream)
  -> CollectionContext (all tables)
	   -> run selected ICollectionRules -> CollectionFindingGroup[]
  -> ScanReport { Tables, CollectionFindings }
```

Rendering: `ReportRenderer.BuildSummaryLines(report)` is the single source of truth.
`FormatSummary` flattens it to text (console); `MainForm.WriteSummary` colors the
`[TAG]` per severity (UI).

## How to extend (common tasks)

- **Add a table rule (code):** implement `ITableRule`, register in
  `VpxRegistryFactory.Build` via `registry.AddTableRule(...)`. Set `Depth` to
  `AnalysisDepth.Quick` if it only reads the file name, otherwise `Deep`.
- **Add a collection rule / integration (DOF/PinUP/VPS-style):** implement
  `ICollectionRule`, register via `registry.AddCollectionRule(...)`. Set
  `EnabledByDefault = false` for opt-in integrations, and set `Depth` (`Quick`
  when it compares names/listings/databases; `Deep` when it needs each table's
  parsed body, e.g. `cGameName`).
- **Add a declarative rule (no code):** add an object to `rules[]` in `rules.json`
  (see `docs/authoring-rules.md`). Supports `severity` = info|warning|error
  (default warning). These are `Deep` (they match parsed elements).
- **Add a new emulator/platform (e.g. Future Pinball):** implement
  `IPinballPlatform` + subclass `PinballTable` and `TableElement`
  (implement `ITimerElement` only if it applies). Override `LoadShallow` if the
  full parse is slow, so quick-only scans stay fast. Register the platform.
  **Touch no Core files.** Universal rules light up automatically; platform-specific
  rules set `SupportedPlatforms`.

## Build & validate

- Build the solution: `dotnet build "VPinInspector.slnx"`.
- Core can build alone (`dotnet build "Core\VPinInspector.Core.csproj"`) and MUST stay
  free of WinForms/OpenMcdf/SQLite/Vpx dependencies.
- There is **no test project** yet; a successful build is the current validation gate.
- If editing in Visual Studio after a project/solution rename, reload
  `VPinInspector.slnx` (stale in-memory solution causes MSB4025 for old paths).

## Conventions & guardrails

- **Keep Core neutral.** No VPX types, no WinForms, no external packages leak into Core.
- **Keep `TableElement` thin.** Prefer capability interfaces + the `Properties` bag over
  fattening the base or subclassing per element type.
- **Rules never do I/O directly** beyond what a collection rule inherently needs
  (folder/db). Table rules read the context.
- **One source of truth for report content** — extend `ReportRenderer`, don't
  re-implement formatting in a front-end.
- Match existing style; avoid gratuitous comments. `.NET 10`, nullable enabled,
  implicit usings enabled.
- **Naming:** only genuinely VPX-specific types keep a `Vpx` prefix. Neutral concepts
  do not.

## Known follow-ups / tech debt

- `Vpx/` legacy helpers still live in the app project; they could move under
  `Platforms/Vpx/` for tidiness (the `Vpx/Rules/RuleEngine.cs` is now just a loader).
- `FastTimerRule` is an unregistered example; delete or wire it up as needed.
- No automated tests — a `VPinInspector.Core.Tests` project would be a good addition.
- Potential future work discussed but not started: a Future Pinball platform, a VPS
  (Virtual Pinball Spreadsheet) reference-data source + version-check rule, and moving
  the VPX helpers fully behind the platform adapter.
