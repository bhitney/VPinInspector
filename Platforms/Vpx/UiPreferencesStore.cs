using System.Text.Json;

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// The set of Settings-panel values that persist between runs. These mirror the
/// editable controls in the main window; anything absent from the saved file
/// falls back to the value loaded from <c>rules.json</c> (or a control default).
/// </summary>
public sealed class UiPreferences
{
    public List<string>? ExcludePatterns { get; set; }
    public List<string>? IncludePatterns { get; set; }
    public string? VpxExecutablePath { get; set; }
    public string? DofConfigPath { get; set; }
    public string? PinupDatabasePath { get; set; }
    public bool? CheckPinupVisibility { get; set; }
    public double? MaxRunTimeSeconds { get; set; }
    public int? SortModeIndex { get; set; }
    public string? ScanFolder { get; set; }
    public bool? Recursive { get; set; }
}

/// <summary>
/// Manages <c>ui_preferences.json</c> kept next to the executable. It records the
/// user's Settings-panel choices (exclude/include globs, sort mode, PinUP cross
/// reference, paths, scan folder, etc.) so they survive between runs. Missing or
/// malformed files are treated as "no saved preferences", and every property is
/// optional so newly added settings fall back to their file/control default.
/// </summary>
public sealed class UiPreferencesStore
{
    /// <summary>The default file name written next to the executable.</summary>
    public const string FileName = "ui_preferences.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Full path to the backing <c>ui_preferences.json</c> file.</summary>
    public string FilePath { get; }

    public UiPreferencesStore(string? filePath = null) =>
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// Loads the saved preferences. Returns an empty <see cref="UiPreferences"/>
    /// (all nulls) when the file is missing or cannot be parsed.
    /// </summary>
    public UiPreferences Load()
    {
        if (!File.Exists(FilePath))
        {
            return new UiPreferences();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UiPreferences>(json) ?? new UiPreferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed or unreadable file is treated as "no saved preferences".
            return new UiPreferences();
        }
    }

    /// <summary>
    /// Persists the given preferences, replacing any existing file. Save failures
    /// are swallowed since the preferences are best-effort.
    /// </summary>
    public void Save(UiPreferences preferences)
    {
        try
        {
            string json = JsonSerializer.Serialize(preferences, SerializerOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; don't crash on save failure.
        }
    }
}
