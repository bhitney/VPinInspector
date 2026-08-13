# Copilot instructions — VPin Inspector

The authoritative context for this repository lives in [AGENTS.md](../AGENTS.md) at
the repo root. **Read it first** before planning or editing.

## Quick orientation

- **Solution:** `VPinInspector.slnx` — two projects:
  - `Core/VPinInspector.Core.csproj` (`net10.0`) — platform-neutral pipeline. No WinForms, no external packages.
  - `VPinInspector.csproj` (`net10.0-windows`) — console + WinForms front-ends, VPX adapter, legacy VPX helpers. References Core.
- **On-disk repo folder is still named `VPX Inspector`** (git path); do not rename it. Project/solution files are `VPinInspector.*`.
- **Build/validate:** `dotnet build "VPinInspector.slnx"`. No test project yet — a clean build is the validation gate.

## Core rules to respect

- Keep **Core neutral**: no VPX types, WinForms, OpenMcdf, or SQLite in `Core/`.
- Rules operate on `TableElement` / `PinballTable` + capability interfaces (e.g. `ITimerElement`), never VPX-specific types.
- **Rules don't do their own file I/O** — `InspectionService` loads each table once and passes a `TableContext`; collection rules get a `CollectionContext`.
- Register new modules in `Platforms/Vpx/VpxRegistryFactory.cs` (`AddTableRule` / `AddCollectionRule`). Opt-in integrations use `EnabledByDefault = false`.
- A new emulator = implement `IPinballPlatform` + subclass `PinballTable`/`TableElement`; **touch no Core files**.
- Severity is standardized: every `Finding` has `FindingSeverity` (Info/Warning/Error). Report content flows through `ReportRenderer` (single source of truth) — don't re-implement formatting in a front-end.
- Declarative rules are authored in `rules.json` (see `docs/authoring-rules.md`).

See [AGENTS.md](../AGENTS.md) for the full source map, data flow, extension recipes, and known follow-ups.
