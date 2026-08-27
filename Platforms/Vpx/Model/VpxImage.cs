using VPin.Inspector.Core.Model;
using VPin.Inspector.Vpx; // reuse existing BiffReader

namespace VPin.Inspector.Platforms.Vpx.Model;

/// <summary>
/// VPX image resource (from a GameStg "Image*" stream). Not a GameItem, but
/// surfaced as a neutral <see cref="TableElement"/> so image-oriented rules
/// (e.g. the PostItNote alpha-mask check) can inspect it like any other element.
/// </summary>
public sealed class VpxImage : TableElement
{
    public override string TypeName => "Image";

    /// <summary>
    /// Property-bag key for the image's Alpha Mask value (BIFF <c>ALTV</c>).
    /// Stored as a <see cref="float"/>. Absent when the record isn't present.
    /// </summary>
    public const string AlphaMaskKey = "AlphaMask";

    /// <summary>
    /// Reads NAME and the alpha-mask value (ALTV) from an Image BIFF stream.
    /// Image streams don't lay out cleanly as a single BIFF record list: the
    /// embedded bitmap (a nested JPEG substream whose raw bytes follow a SIZE
    /// record) breaks a naive sequential walk, and ALTV lives AFTER that blob.
    /// So we locate the tags directly, the way the script's CODE record is found.
    /// </summary>
    public static VpxImage Parse(string streamName, byte[] bytes)
    {
        string name = FindNameTag(bytes) ?? string.Empty;
        float? alphaMask = FindFloatTag(bytes, "ALTV"u8);

        var properties = new Dictionary<string, object?>();
        if (alphaMask is not null)
        {
            properties[AlphaMaskKey] = alphaMask.Value;
        }

        return new VpxImage
        {
            Id = streamName,
            Name = name,
            Properties = properties,
        };
    }

    /// <summary>
    /// Finds the first NAME record and decodes its string. Unlike GameItem
    /// names (UTF-16), Image NAME data is a leading Int32 byte-count followed by
    /// single-byte (Latin1) characters.
    /// </summary>
    private static string? FindNameTag(byte[] bytes)
    {
        int tagPos = IndexOfTag(bytes, "NAME"u8, 0);
        if (tagPos < 0 || tagPos + 8 > bytes.Length)
        {
            return null;
        }

        int lenPos = tagPos + 4;
        int byteCount = BitConverter.ToInt32(bytes, lenPos);
        int dataPos = lenPos + 4;
        if (byteCount < 0 || dataPos + byteCount > bytes.Length)
        {
            return null;
        }

        return System.Text.Encoding.Latin1.GetString(bytes, dataPos, byteCount);
    }

    /// <summary>
    /// Finds a 4-char tag and reads the 4 bytes after it as a little-endian
    /// float. Returns null when the tag isn't present.
    /// </summary>
    private static float? FindFloatTag(byte[] bytes, ReadOnlySpan<byte> tag)
    {
        int tagPos = IndexOfTag(bytes, tag, 0);
        if (tagPos < 0 || tagPos + 8 > bytes.Length)
        {
            return null;
        }

        return BitConverter.ToSingle(bytes, tagPos + 4);
    }

    private static int IndexOfTag(byte[] bytes, ReadOnlySpan<byte> tag, int start)
    {
        for (int i = start; i + tag.Length <= bytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < tag.Length; j++)
            {
                if (bytes[i + j] != tag[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
