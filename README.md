# VPX Inspector

A utility for inspecting Visual Pinball 10.x table files (`.vpx`) and flagging
common configuration issues via a declarative rule set.

A `.vpx` file is an OLE2 / MS-CFB compound file. VPX Inspector reads the element
(`GameItem`) streams and the table script directly — no manual extraction
required — parses the BIFF records, and evaluates user-defined rules against the
elements.

## Features

- Reads packed `.vpx` files directly (via OpenMcdf) or already-extracted folders.
- Parses element BIFF records for name, type, timer enabled (`TMON`) and timer
  interval (`TMIN`).
- Declarative rules in `rules.json`: name globs, element-type filters, timer
  requirement, interval conditions (`>=10`, `<10`, `>40`, ...), suggested value,
  and per-rule `enabled` toggle.
- General settings: `maxRunTimeSeconds` (time-boxed scans) and `excludePatterns`
  (skip files by name glob, e.g. `VR ROOM*`).
- Extracts the script (`CODE` stream), resolves `cGameName`, and reports
  duplicate game names across a scan.
- Console mode (batch reporting with a summary checklist) and a WinForms GUI
  (folder picker, checkboxed rules tree, editable settings, scan / rescan
  flagged / reload rules).

## Usage

### GUI

Run with no arguments to launch the graphical interface.

### Console

```
VPX Inspector <path-to-vpx-file-or-folder> [rules.json]
```

- If the path is a `.vpx` file, only that file is scanned.
- If the path is a folder, all `.vpx` files within it are scanned recursively.

## rules.json

```json
{
  "settings": {
	"maxRunTimeSeconds": 0,
	"excludePatterns": [ "VR ROOM*" ]
  },
  "rules": [
	{
	  "id": "ball-shadow",
	  "description": "Ball shadow update timer is misconfigured",
	  "enabled": true,
	  "namePatterns": [ "BallShadowUpdate", "*shadow*" ],
	  "types": [ "Timer" ],
	  "mustBeTimer": true,
	  "interval": ">=10",
	  "suggest": -1
	}
  ]
}
```

## Build

Targets .NET 10 (`net10.0-windows`, WinForms enabled for the GUI).

```
dotnet build
```
