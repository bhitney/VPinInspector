using System.Text.Json;

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// Manages <c>rule_selection.json</c> kept next to the executable. It records
/// which rules (by id) the user has checked or unchecked in the rules tree so
/// the selection survives between runs. The file is a JSON object mapping a rule
/// id to a boolean (<c>{"ball-shadow-routine": false}</c>). Rules absent from
/// the file fall back to their <c>EnabledByDefault</c> value, so newly added
/// rules are not accidentally suppressed.
/// </summary>
public sealed class RuleSelectionStore
{
    /// <summary>The default file name written next to the executable.</summary>
    public const string FileName = "rule_selection.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Full path to the backing <c>rule_selection.json</c> file.</summary>
    public string FilePath { get; }

    public RuleSelectionStore(string? filePath = null) =>
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// Loads the saved rule id -> checked map (case-insensitive on id). Returns
    /// an empty map when the file is missing or cannot be parsed.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Load()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(FilePath))
        {
            return map;
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            Dictionary<string, bool>? saved =
                JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (saved is not null)
            {
                foreach (KeyValuePair<string, bool> entry in saved)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key))
                    {
                        map[entry.Key.Trim()] = entry.Value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed or unreadable file is treated as "no saved selection".
        }

        return map;
    }

    /// <summary>
    /// Persists the given rule id -> checked map, replacing any existing file.
    /// Save failures are swallowed since the selection is best-effort.
    /// </summary>
    public void Save(IReadOnlyDictionary<string, bool> selection)
    {
        try
        {
            string json = JsonSerializer.Serialize(selection, SerializerOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; don't crash on save failure.
        }
    }
}
