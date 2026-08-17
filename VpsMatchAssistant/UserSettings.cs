using System.Text.Json;

namespace VPin.MatchAssistant;

/// <summary>
/// Persisted user preferences (window geometry, last-used paths). Stored as JSON
/// under %AppData%\VpsMatchAssistant\settings.json.
/// </summary>
public sealed class UserSettings
{
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool Maximized { get; set; }

    public string? DbPath { get; set; }
    public string? CsvPath { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VpsMatchAssistant",
        "settings.json");

    public static UserSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings are best-effort; don't crash on save failure.
        }
    }
}
