using OpenMcdf;

namespace VPin.Inspector.Vpx;

/// <summary>
/// Opens a .vpx file (an OLE2 / MS-CFB compound file) directly and reads the
/// script from the GameStg storage. Element parsing now lives in the VPX
/// platform adapter (VpxPlatform); this retains only script extraction.
/// </summary>
public static class VpxCompoundFile
{
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
    /// Embedded, author-supplied table metadata read from the file's own
    /// <c>TableInfo</c> storage. These values are frequently stale or wrong, so
    /// callers treat them as "as embedded in the file" rather than authoritative.
    /// Any field is null when its stream is absent or empty.
    /// </summary>
    public readonly record struct EmbeddedTableInfo(string? TableName, string? Author, string? Version);

    /// <summary>
    /// Reads the <c>TableInfo</c> storage (TableName, AuthorName, TableVersion)
    /// from a .vpx file. The streams hold UTF-16 (Unicode) text. Returns empty
    /// fields when the storage or a given stream is missing.
    /// </summary>
    public static EmbeddedTableInfo GetTableInfo(string vpxFilePath)
    {
        using var root = RootStorage.OpenRead(vpxFilePath);

        Storage tableInfo;
        try
        {
            tableInfo = root.OpenStorage("TableInfo");
        }
        catch (Exception)
        {
            return new EmbeddedTableInfo(null, null, null);
        }

        return new EmbeddedTableInfo(
            ReadUnicodeStream(tableInfo, "TableName"),
            ReadUnicodeStream(tableInfo, "AuthorName"),
            ReadUnicodeStream(tableInfo, "TableVersion"));
    }

    private static string? ReadUnicodeStream(Storage storage, string streamName)
    {
        try
        {
            using CfbStream stream = storage.OpenStream(streamName);
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            string value = System.Text.Encoding.Unicode.GetString(bytes).Trim('\0', ' ', '\r', '\n', '\t');
            return value.Length == 0 ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
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
