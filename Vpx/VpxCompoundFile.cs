using OpenMcdf;

namespace VPX_Inspector.Vpx;

/// <summary>
/// Opens a .vpx file (an OLE2 / MS-CFB compound file) directly and reads the
/// element streams from the GameStg storage — no manual extraction required.
/// </summary>
public static class VpxCompoundFile
{
    /// <summary>
    /// Parses every GameItem stream inside the .vpx compound file.
    /// </summary>
    /// <param name="vpxFilePath">Full path to the .vpx file.</param>
    public static IReadOnlyList<GameItem> ScanGameItems(string vpxFilePath)
    {
        var items = new List<GameItem>();

        using var root = RootStorage.OpenRead(vpxFilePath);
        Storage gameStg = root.OpenStorage("GameStg");

        foreach (var entry in gameStg.EnumerateEntries())
        {
            if (entry.Type != EntryType.Stream ||
                !entry.Name.StartsWith("GameItem", StringComparison.Ordinal))
            {
                continue;
            }

            using CfbStream stream = gameStg.OpenStream(entry.Name);
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            items.Add(GameItem.Parse(entry.Name, bytes));
        }

        return items;
    }

    /// <summary>
    /// Returns timer-bearing elements, shortest interval first.
    /// </summary>
    public static IReadOnlyList<GameItem> FindTimers(string vpxFilePath) =>
        ScanGameItems(vpxFilePath)
            .Where(i => i.HasTimer)
            .OrderBy(i => i.TimerIntervalMs)
            .ToList();

    /// <summary>
    /// Finds a specific element by (case-insensitive) name.
    /// </summary>
    public static GameItem? FindByName(string vpxFilePath, string name) =>
        ScanGameItems(vpxFilePath)
            .FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extracts the table's VBScript from the GameData stream's <c>CODE</c> BIFF
    /// record. Returns an empty string when no script is present.
    /// </summary>
    public static string GetScript(string vpxFilePath)
    {
        using var root = RootStorage.OpenRead(vpxFilePath);
        Storage gameStg = root.OpenStorage("GameStg");

        using CfbStream stream = gameStg.OpenStream("GameData");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        return ExtractCodeRecord(bytes);
    }

    /// <summary>
    /// Locates the <c>CODE</c> record within a GameData byte buffer and returns
    /// its payload as text. The record is laid out as the 4-char tag "CODE"
    /// followed by an Int32 length and then that many bytes of script.
    /// Script bytes are single-byte (Latin1), not UTF-16.
    /// </summary>
    private static string ExtractCodeRecord(byte[] bytes)
    {
        // Find the ASCII tag "CODE".
        ReadOnlySpan<byte> tag = "CODE"u8;
        for (int i = 0; i + tag.Length + 4 <= bytes.Length; i++)
        {
            if (bytes[i] == tag[0] &&
                bytes[i + 1] == tag[1] &&
                bytes[i + 2] == tag[2] &&
                bytes[i + 3] == tag[3])
            {
                int lenPos = i + 4;
                int length = BitConverter.ToInt32(bytes, lenPos);
                int dataPos = lenPos + 4;

                if (length < 0 || dataPos + length > bytes.Length)
                {
                    length = Math.Max(0, bytes.Length - dataPos);
                }

                return System.Text.Encoding.Latin1.GetString(bytes, dataPos, length);
            }
        }

        return string.Empty;
    }
}
