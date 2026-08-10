using System.Text;

namespace VPX_Inspector.Vpx;

/// <summary>
/// A single BIFF record: a 4-character tag plus its raw payload bytes.
/// VPX GameItem streams are a sequence of these records, each laid out as:
///   [Int32 size][4 ASCII tag chars][size-4 bytes of data]
/// </summary>
public sealed class BiffRecord
{
    public required string Tag { get; init; }

    /// <summary>Raw payload bytes (everything after the 4 tag chars).</summary>
    public required byte[] Data { get; init; }

    /// <summary>Reads the payload as a little-endian Int32 (used for TMIN etc.).</summary>
    public int AsInt32() => Data.Length >= 4 ? BitConverter.ToInt32(Data, 0) : 0;

    /// <summary>Reads the payload as a 4-byte boolean flag (used for TMON etc.).</summary>
    public bool AsBool() => AsInt32() != 0;

    /// <summary>Reads the payload as a little-endian float.</summary>
    public float AsFloat() => Data.Length >= 4 ? BitConverter.ToSingle(Data, 0) : 0f;

    /// <summary>
    /// Reads the payload as a VPX wide string. Strings are stored as a
    /// leading Int32 byte-count followed by UTF-16 (little-endian) characters.
    /// </summary>
    public string AsWideString()
    {
        if (Data.Length < 4)
        {
            return string.Empty;
        }

        int byteCount = BitConverter.ToInt32(Data, 0);
        byteCount = Math.Clamp(byteCount, 0, Data.Length - 4);
        return Encoding.Unicode.GetString(Data, 4, byteCount);
    }
}

/// <summary>
/// Parses the BIFF record stream contained in a VPX GameItem storage stream.
/// </summary>
public static class BiffReader
{
    /// <summary>
    /// Reads all BIFF records from a GameItem stream. The first 4 bytes of the
    /// stream are the element type and are returned via <paramref name="itemType"/>.
    /// </summary>
    public static IReadOnlyList<BiffRecord> Read(byte[] bytes, out int itemType)
    {
        var records = new List<BiffRecord>();
        itemType = 0;

        if (bytes.Length < 4)
        {
            return records;
        }

        // Leading Int32 is the element (item) type.
        itemType = BitConverter.ToInt32(bytes, 0);
        int pos = 4;

        while (pos + 4 <= bytes.Length)
        {
            int size = BitConverter.ToInt32(bytes, pos);
            pos += 4;

            // A size that can't hold the 4-char tag terminates the stream (ENDB or padding).
            if (size < 4 || pos + size > bytes.Length + 4)
            {
                break;
            }

            if (pos + 4 > bytes.Length)
            {
                break;
            }

            string tag = Encoding.ASCII.GetString(bytes, pos, 4);
            int dataLength = size - 4;

            // Clamp defensively against malformed streams.
            if (pos + 4 + dataLength > bytes.Length)
            {
                dataLength = Math.Max(0, bytes.Length - (pos + 4));
            }

            var data = new byte[dataLength];
            Array.Copy(bytes, pos + 4, data, 0, dataLength);

            records.Add(new BiffRecord { Tag = tag, Data = data });

            pos += 4 + dataLength;

            if (tag == "ENDB")
            {
                break;
            }
        }

        return records;
    }
}
