using System.Text;

namespace VPin.Inspector.Vps;

/// <summary>
/// One row of the VPS <c>puplookup.csv</c> reduced to the columns we care about.
/// Values are keyed by the header name (as configured), trimmed, with empty
/// values normalized to null.
/// </summary>
public sealed class PupLookupRow
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    public PupLookupRow(IReadOnlyDictionary<string, string?> values) => _values = values;

    /// <summary>Returns the value for <paramref name="column"/> or null when absent/empty.</summary>
    public string? Get(string column) =>
        _values.TryGetValue(column, out string? value) ? value : null;
}

/// <summary>
/// Loads the VPS <c>puplookup.csv</c> reference file. Only the configured
/// columns are retained per row (defaults: GameName, Manufact, GameYear,
/// GAMEVER). The file has a header row; parsing is quote-aware (fields may
/// contain commas and quoted quotes).
/// </summary>
public sealed class PupLookupTable
{
    /// <summary>The rows loaded from the CSV (one per file/version entry).</summary>
    public IReadOnlyList<PupLookupRow> Rows { get; }

    /// <summary>The header columns retained (intersection of file headers and requested columns).</summary>
    public IReadOnlyList<string> Columns { get; }

    private PupLookupTable(IReadOnlyList<PupLookupRow> rows, IReadOnlyList<string> columns)
    {
        Rows = rows;
        Columns = columns;
    }

    /// <summary>Default columns of interest for game matching.</summary>
    public static readonly IReadOnlyList<string> DefaultColumns = new[]
    {
        "GameName",
        "Manufact",
        "GameYear",
        "GAMEVER",
    };

    /// <summary>
    /// Loads and parses the CSV at <paramref name="path"/>, keeping only the
    /// requested <paramref name="columns"/> (defaults to <see cref="DefaultColumns"/>).
    /// Column matching against the header is case-insensitive.
    /// </summary>
    public static PupLookupTable Load(string path, IReadOnlyList<string>? columns = null)
    {
        columns ??= DefaultColumns;

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? headerLine = ReadRecord(reader, out List<string> header);
        if (headerLine is null)
        {
            return new PupLookupTable(Array.Empty<PupLookupRow>(), Array.Empty<string>());
        }

        // Map each requested column to its index in the header (case-insensitive).
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            columnIndex[header[i].Trim()] = i;
        }

        var kept = columns
            .Where(c => columnIndex.ContainsKey(c))
            .ToList();

        var rows = new List<PupLookupRow>();
        while (ReadRecord(reader, out List<string> fields) is not null)
        {
            if (fields.Count == 0)
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (string column in kept)
            {
                int index = columnIndex[column];
                string? value = index < fields.Count ? fields[index].Trim() : null;
                values[column] = string.IsNullOrEmpty(value) ? null : value;
            }

            rows.Add(new PupLookupRow(values));
        }

        return new PupLookupTable(rows, kept);
    }

    /// <summary>
    /// Reads one logical CSV record (which may span multiple physical lines when a
    /// quoted field contains newlines) into <paramref name="fields"/>. Returns the
    /// raw record text, or null at end of stream.
    /// </summary>
    private static string? ReadRecord(TextReader reader, out List<string> fields)
    {
        fields = new List<string>();

        string? line = reader.ReadLine();
        if (line is null)
        {
            return null;
        }

        var current = new StringBuilder();
        var raw = new StringBuilder(line);
        bool inQuotes = false;
        int i = 0;

        while (true)
        {
            if (i >= line.Length)
            {
                if (inQuotes)
                {
                    // Quoted field continues on the next physical line.
                    string? next = reader.ReadLine();
                    if (next is null)
                    {
                        break;
                    }

                    current.Append('\n');
                    raw.Append('\n').Append(next);
                    line = next;
                    i = 0;
                    continue;
                }

                break;
            }

            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                current.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    fields.Add(current.ToString());
                    current.Clear();
                    i++;
                    break;
                default:
                    current.Append(c);
                    i++;
                    break;
            }
        }

        fields.Add(current.ToString());
        return raw.ToString();
    }
}
