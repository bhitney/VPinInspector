using System.Text.Json.Serialization;

namespace VPin.Inspector.Vpx.Rules;

/// <summary>
/// The root object deserialized from rules.json.
/// </summary>
public sealed class RuleSet
{
    [JsonPropertyName("settings")]
    public InspectionSettings Settings { get; init; } = new();

    [JsonPropertyName("rules")]
    public List<InspectionRule> Rules { get; init; } = new();
}

/// <summary>
/// General (non-rule) scan settings read from the "settings" object in rules.json.
/// </summary>
public sealed class InspectionSettings
{
    /// <summary>
    /// Optional maximum wall-clock scan time in seconds. When greater than zero,
    /// scanning stops once the elapsed time exceeds this budget (useful for
    /// testing against a subset without scanning everything). Zero/absent = no limit.
    /// </summary>
    [JsonPropertyName("maxRunTimeSeconds")]
    public double MaxRunTimeSeconds { get; init; }

    /// <summary>
    /// Maximum number of tables parsed concurrently. Table parsing and rule
    /// evaluation are independent per file, so scanning a folder scales well
    /// across cores/NVMe. Zero or negative = use <see cref="Environment.ProcessorCount"/>;
    /// 1 = the legacy single-threaded behavior. The final report (and summary)
    /// stays in the same order regardless of this value.
    /// </summary>
    [JsonPropertyName("maxDegreeOfParallelism")]
    public int MaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// Optional file-name globs to exclude from scanning, e.g. [ "VR ROOM*" ].
    /// Supports '*' and '?' wildcards, matched case-insensitively against the
    /// table's file name (without directory).
    /// </summary>
    [JsonPropertyName("excludePatterns")]
    public List<string> ExcludePatterns { get; init; } = new();

    /// <summary>
    /// Full path to the Visual Pinball executable used to open a table when its
    /// name is clicked in the summary checklist. The table is launched as
    /// <c>vpinballx64.exe -edit "&lt;full table path&gt;"</c>. Empty = not configured.
    /// </summary>
    [JsonPropertyName("vpxExecutablePath")]
    public string VpxExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Path to the DirectOutput Framework (DOF) configuration .ini used by the
    /// "dof-check" deep-analysis rule. When empty, the standard location
    /// (<c>C:\DirectOutput\Config\directoutputconfig51.ini</c>) is used if it
    /// exists.
    /// </summary>
    [JsonPropertyName("dofConfigPath")]
    public string DofConfigPath { get; init; } = string.Empty;

    /// <summary>
    /// Path to the PinUP Popper SQLite database (PUPDatabase.db), shared by every
    /// PinUP check (quick and deep) and the visibility lookup. Defaults to the
    /// standard install location when empty.
    /// </summary>
    [JsonPropertyName("databasePath")]
    public string PinupDatabasePath { get; init; } = @"C:\vPinball\PinUPSystem\PUPDatabase.db";

    /// <summary>
    /// When true, the deep-analysis summary annotates each flagged table with its
    /// current PinUP Popper visibility status (Disabled/Visible/Mature/WIP),
    /// resolved by matching the table's file name to the Games table's
    /// GameFileName column. When false, PinUP is not consulted for visibility.
    /// </summary>
    [JsonPropertyName("checkPinupVisibility")]
    public bool CheckPinupVisibility { get; init; }

    /// <summary>
    /// Configuration (collection-scope) checks, keyed by check id. Each check has
    /// its own strongly-typed settings block.
    /// </summary>
    [JsonPropertyName("configurationChecks")]
    public ConfigurationChecksSettings ConfigurationChecks { get; init; } = new();
}

/// <summary>
/// Container for the settings of each configuration check.
/// </summary>
public sealed class ConfigurationChecksSettings
{
    /// <summary>Settings for the PinUP Popper game-match check.</summary>
    [JsonPropertyName("pinup-game-match")]
    public PinupMatchSettings PinupGameMatch { get; init; } = new();

    /// <summary>Settings for the PinUP Popper metadata comparison check.</summary>
    [JsonPropertyName("pinup-metadata-check")]
    public PinupMetadataCheckSettings PinupMetadataCheck { get; init; } = new();

    /// <summary>Settings for the PinUP Popper media-match check.</summary>
    [JsonPropertyName("media-match")]
    public PinupMediaMatchSettings MediaMatch { get; init; } = new();

    /// <summary>Settings for the VR ROOM matching check.</summary>
    [JsonPropertyName("vr-room-matching")]
    public VrRoomMatchSettings VrRoomMatching { get; init; } = new();

    /// <summary>Settings for the PinUP hygiene check (Popper Games vs VPS puplookup.csv).</summary>
    [JsonPropertyName("pup-hygiene")]
    public PupHygieneSettings PupHygiene { get; init; } = new();

    /// <summary>Settings for the version check (local Popper GAMEVER vs VPS puplookup.csv).</summary>
    [JsonPropertyName("version-check")]
    public VersionCheckSettings VersionCheck { get; init; } = new();

    /// <summary>
    /// Settings for the configurable shadow depth-mask check: a user-editable
    /// list of regexes for locating shadow primitives whose "Hide parts behind"
    /// flag should be unchecked. Disabled by default (likely noisy).
    /// </summary>
    [JsonPropertyName("configurable-shadow")]
    public ConfigurableShadowSettings ConfigurableShadow { get; init; } = new();
}

/// <summary>
/// Common settings shared by all configuration checks.
/// </summary>
public abstract class ConfigurationCheckSettings
{
    /// <summary>Whether the check runs. Defaults to false.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>
    /// When true, the check honors the active exclude globs (e.g. skips
    /// "VR ROOM*"). When false (default) it sees the raw filesystem, which is
    /// usually what database-vs-disk comparisons want.
    /// </summary>
    [JsonPropertyName("respectExcludePatterns")]
    public bool RespectExcludePatterns { get; init; }
}

/// <summary>
/// Settings for the PinUP Popper game-match check (folder vs database).
/// </summary>
public sealed class PinupMatchSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// Explicit emulator IDs (EMUID) to include, e.g. [ 1, 7, 10 ]. May be empty
    /// when relying on <see cref="MatchEmulatorsByFolder"/>.
    /// </summary>
    [JsonPropertyName("emulatorIds")]
    public List<int> EmulatorIds { get; init; } = new();

    /// <summary>
    /// When true, also include any emulator whose DirGames points at the folder
    /// being scanned (path-normalized comparison). Defaults to true.
    /// </summary>
    [JsonPropertyName("matchEmulatorsByFolder")]
    public bool MatchEmulatorsByFolder { get; init; } = true;

    /// <summary>
    /// When true, only consider games/emulators marked Visible in the database.
    /// Defaults to false (consider all).
    /// </summary>
    [JsonPropertyName("visibleOnly")]
    public bool VisibleOnly { get; init; }
}

/// <summary>
/// Settings for the PinUP Popper metadata comparison check: compares each table's
/// inferred/embedded ROM (cGameName), Manufacturer, and Year against the values
/// recorded in the PinUP Popper Games table (ROM, Manufact, GameYear). A deep
/// check (needs each table's parsed cGameName).
/// </summary>
public sealed class PinupMetadataCheckSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// Explicit emulator IDs (EMUID) to include, e.g. [ 1, 7, 10 ]. May be empty
    /// when relying on <see cref="MatchEmulatorsByFolder"/>.
    /// </summary>
    [JsonPropertyName("emulatorIds")]
    public List<int> EmulatorIds { get; init; } = new();

    /// <summary>
    /// When true, also include any emulator whose DirGames points at the folder
    /// being scanned (path-normalized comparison). Defaults to true.
    /// </summary>
    [JsonPropertyName("matchEmulatorsByFolder")]
    public bool MatchEmulatorsByFolder { get; init; } = true;

    /// <summary>
    /// When true, only consider games/emulators marked Visible in the database.
    /// Defaults to false (consider all).
    /// </summary>
    [JsonPropertyName("visibleOnly")]
    public bool VisibleOnly { get; init; }
}

/// <summary>
/// Settings for the PinUP Popper media-match check: verifies that Popper media
/// exists for each registered game, per selected media folder (Playfield, Topper,
/// BackGlass, DMD, Loading, Menu). Media lives under the emulator's DirMedia
/// (or the GlobalSettings GlobalMediaDir fallback), in a per-type subfolder, named
/// after the game file with any extension (video or image).
/// </summary>
public sealed class PinupMediaMatchSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// Explicit emulator IDs (EMUID) to include, e.g. [ 1, 7, 10 ]. When any are
    /// supplied they take precedence and <see cref="MatchEmulatorsByFolder"/> is
    /// ignored, so the whole Popper database can be checked regardless of where the
    /// source .vpx files sit. May be empty to fall back to folder matching.
    /// </summary>
    [JsonPropertyName("emulatorIds")]
    public List<int> EmulatorIds { get; init; } = new();

    /// <summary>
    /// When true and <see cref="EmulatorIds"/> is empty, include any emulator whose
    /// DirGames points at the folder being scanned (path-normalized comparison).
    /// Ignored when explicit emulator IDs are supplied. Defaults to true.
    /// </summary>
    [JsonPropertyName("matchEmulatorsByFolder")]
    public bool MatchEmulatorsByFolder { get; init; } = true;

    /// <summary>
    /// When true, only consider games/emulators marked Visible in the database.
    /// Defaults to false (consider all).
    /// </summary>
    [JsonPropertyName("visibleOnly")]
    public bool VisibleOnly { get; init; }

    /// <summary>
    /// Media subfolders (relative to the resolved media directory) to check, e.g.
    /// [ "Playfield", "Topper", "BackGlass", "DMD", "Loading", "Menu" ]. Each is
    /// checked independently so a user can target just one media type. Defaults to
    /// Playfield when empty.
    /// </summary>
    [JsonPropertyName("mediaFolders")]
    public List<string> MediaFolders { get; init; } = new() { "Playfield" };
}

/// <summary>
/// Settings for the VR ROOM matching check: every "VR ROOM &lt;name&gt;.vpx" should
/// have a matching non-VR "&lt;name&gt;.vpx" in the same folder.
/// </summary>
public sealed class VrRoomMatchSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// The filename prefix that denotes a VR ROOM variant. Defaults to "VR ROOM ".
    /// Matched case-insensitively.
    /// </summary>
    [JsonPropertyName("prefix")]
    public string Prefix { get; init; } = "VR ROOM ";
}

/// <summary>
/// Settings for the PinUP hygiene check: cross-checks each PinUP Popper Games
/// entry against the VPS <c>puplookup.csv</c> reference file, matching
/// manufacturer and year exactly and fuzzy-matching the game name.
/// </summary>
public sealed class PupHygieneSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// Explicit emulator IDs (EMUID) to include, e.g. [ 1, 7, 10 ]. May be empty
    /// when relying on <see cref="MatchEmulatorsByFolder"/>.
    /// </summary>
    [JsonPropertyName("emulatorIds")]
    public List<int> EmulatorIds { get; init; } = new();

    /// <summary>
    /// When true, also include any emulator whose DirGames points at the folder
    /// being scanned (path-normalized comparison). Defaults to true.
    /// </summary>
    [JsonPropertyName("matchEmulatorsByFolder")]
    public bool MatchEmulatorsByFolder { get; init; } = true;

    /// <summary>
    /// When true, only consider games/emulators marked Visible in the database.
    /// Defaults to false (consider all).
    /// </summary>
    [JsonPropertyName("visibleOnly")]
    public bool VisibleOnly { get; init; }

    /// <summary>
    /// Optional override path to the VPS puplookup.csv. When empty, the file in
    /// the application directory is used (downloaded on demand when missing).
    /// </summary>
    [JsonPropertyName("lookupCsvPath")]
    public string LookupCsvPath { get; init; } = string.Empty;

    /// <summary>
    /// The puplookup.csv columns to load. Defaults to GameName, Manufact,
    /// GameYear, GAMEVER, WEBGameID. Configurable so columns can be added/removed
    /// later. WEBGameID is the definitive match key when populated.
    /// </summary>
    [JsonPropertyName("lookupColumns")]
    public List<string> LookupColumns { get; init; } =
        new() { "GameName", "Manufact", "GameYear", "GAMEVER", "WEBGameID" };
}

/// <summary>
/// Settings for the version check: compares each PinUP Popper game's local
/// version (GAMEVER) against the newest matching version in the VPS
/// puplookup.csv, flagging mismatches (online newer / local newer / unknown).
/// </summary>
public sealed class VersionCheckSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// Explicit emulator IDs (EMUID) to include, e.g. [ 1, 7, 10 ]. May be empty
    /// when relying on <see cref="MatchEmulatorsByFolder"/>.
    /// </summary>
    [JsonPropertyName("emulatorIds")]
    public List<int> EmulatorIds { get; init; } = new();

    /// <summary>
    /// When true, also include any emulator whose DirGames points at the folder
    /// being scanned (path-normalized comparison). Defaults to true.
    /// </summary>
    [JsonPropertyName("matchEmulatorsByFolder")]
    public bool MatchEmulatorsByFolder { get; init; } = true;

    /// <summary>
    /// When true, only consider games/emulators marked Visible in the database.
    /// Defaults to false (consider all).
    /// </summary>
    [JsonPropertyName("visibleOnly")]
    public bool VisibleOnly { get; init; }

    /// <summary>
    /// Optional override path to the VPS puplookup.csv. When empty, the file in
    /// the application directory is used (downloaded on demand when missing).
    /// </summary>
    [JsonPropertyName("lookupCsvPath")]
    public string LookupCsvPath { get; init; } = string.Empty;
}

/// <summary>
/// Settings for the configurable shadow depth-mask check. Scans primitive
/// elements whose name matches any of the user-supplied <see cref="Patterns"/>
/// (.NET regular expressions, evaluated case-insensitively) and flags those
/// whose "Hide parts behind" (BIFF ZMSK) flag is checked. Disabled by default
/// because a broad pattern such as ".*shadow.*" can be noisy.
/// </summary>
public sealed class ConfigurableShadowSettings : ConfigurationCheckSettings
{
    /// <summary>
    /// User-editable list of regexes matched against a primitive's name. Any
    /// match makes the primitive eligible. Examples: "shadow" (broad),
    /// "Divertershadow_(Left|Right)" (targeted). Defaults to a single broad
    /// "shadow" pattern.
    /// </summary>
    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; init; } = new() { "shadow" };
}

/// <summary>
/// A single user-defined rule describing which elements to match and what
/// interval condition flags a problem.
/// </summary>
public sealed class InspectionRule
{
    /// <summary>Stable identifier, e.g. "ball-shadow".</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable explanation shown when the rule matches.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the rule participates in scans. Defaults to true. Can be toggled
    /// per-run from the UI without persisting back to the file.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Name variants to match. Supports '*' and '?' wildcards, matched
    /// case-insensitively (e.g. "BallShadow", "*sling*").
    /// </summary>
    [JsonPropertyName("namePatterns")]
    public List<string> NamePatterns { get; init; } = new();

    /// <summary>When true, only elements that carry a timer are considered.</summary>
    [JsonPropertyName("mustBeTimer")]
    public bool MustBeTimer { get; init; }

    /// <summary>
    /// Optional element-type filter, e.g. [ "Timer" ] or [ "Timer", "Light" ].
    /// Matched case-insensitively against the element's type name. When empty,
    /// elements of any type are considered.
    /// </summary>
    [JsonPropertyName("types")]
    public List<string> Types { get; init; } = new();

    /// <summary>
    /// Interval condition applied to the timer interval in milliseconds,
    /// expressed as an operator + number, e.g. "&gt;=10", "&lt;10", "&gt;40".
    /// When omitted, any matching element is reported.
    /// </summary>
    [JsonPropertyName("interval")]
    public string? Interval { get; init; }

    /// <summary>
    /// Optional recommended timer interval in milliseconds for elements that
    /// match this rule. Surfaced on the finding's details and used by
    /// auto-fixers (e.g. the slingshot fix) as the value written back to the
    /// table when a fix is applied.
    /// </summary>
    [JsonPropertyName("suggest")]
    public int? Suggest { get; init; }

    /// <summary>
    /// Severity reported when this rule matches: "info", "warning", or "error"
    /// (case-insensitive). Defaults to "warning" when omitted or unrecognized.
    /// </summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; init; }
}
