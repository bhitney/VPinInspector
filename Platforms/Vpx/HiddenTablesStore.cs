using System.Text.Json;

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// Manages the user-editable <c>hidden_tables.json</c> file kept next to the
/// executable. It stores the file names of tables the user has chosen to hide
/// from future scans so recurring false positives stop resurfacing. The file is
/// a simple JSON array of file names (e.g. <c>["Table.vpx"]</c>) and is the
/// user's to manage by hand.
/// </summary>
public sealed class HiddenTablesStore
{
    /// <summary>The default file name written next to the executable.</summary>
    public const string FileName = "hidden_tables.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Full path to the backing <c>hidden_tables.json</c> file.</summary>
    public string FilePath { get; }

    public HiddenTablesStore(string? filePath = null) =>
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// Loads the set of hidden table file names (case-insensitive). Returns an
    /// empty set when the file is missing or cannot be parsed.
    /// </summary>
    public IReadOnlySet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(FilePath))
        {
            return set;
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            string[]? names = JsonSerializer.Deserialize<string[]>(json);
            if (names is not null)
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        set.Add(name.Trim());
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed or unreadable file is treated as "nothing hidden";
            // the user manages this file themselves.
        }

        return set;
    }

    /// <summary>
    /// Adds <paramref name="fileName"/> (a table file name such as
    /// "Table.vpx") to the hidden list and persists it. Returns true when the
    /// entry was newly added, false when it was already present.
    /// </summary>
    public bool Add(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string name = Path.GetFileName(fileName.Trim());
        var set = new HashSet<string>(Load(), StringComparer.OrdinalIgnoreCase);
        if (!set.Add(name))
        {
            return false;
        }

        Save(set);
        return true;
    }

    /// <summary>
    /// Removes <paramref name="fileName"/> (a table file name such as
    /// "Table.vpx") from the hidden list and persists the change. Returns true
    /// when the entry was present and removed, false when it was not hidden.
    /// </summary>
    public bool Remove(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string name = Path.GetFileName(fileName.Trim());
        var set = new HashSet<string>(Load(), StringComparer.OrdinalIgnoreCase);
        if (!set.Remove(name))
        {
            return false;
        }

        Save(set);
        return true;
    }

    private void Save(IEnumerable<string> names)
    {
        string json = JsonSerializer.Serialize(
            names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
            SerializerOptions);
        File.WriteAllText(FilePath, json);
    }
}
