using OpenMcdf;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// Applies a small, fixed set of in-place corrections to a .vpx file for the
/// findings this tool can safely auto-fix. Only four rules are supported; every
/// other finding is left for the user to correct by hand in VPX.
///
/// Corrections are simple, fixed-width BIFF-record edits (no change in stream
/// length), so the file layout is preserved. Per the analysis in
/// <c>docs/VPXHash.md</c>, the <c>GameStg\MAC</c> integrity digest is not
/// enforced on load, so it is intentionally left stale (never removed).
/// </summary>
public static class VpxCorrectionWriter
{
    /// <summary>Rule ids this writer knows how to auto-fix.</summary>
    public static readonly IReadOnlySet<string> FixableRuleIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "ball-shadow-depth-mask", // primitive ZMSK -> 0 (uncheck "Hide parts behind")
        "ball-shadow",            // timer TMIN -> -1
        "slingshot",              // surface timer TMIN -> 30
        "postitnote-alpha-mask",  // image ALTV -> 50
    };

    /// <summary>True when the table has at least one finding this writer can fix.</summary>
    public static bool HasFixableFinding(TableReport table) =>
        !table.Failed && table.Findings.Any(f => FixableRuleIds.Contains(f.RuleId));

    /// <summary>
    /// Applies every fixable finding for the given table in place. Returns the
    /// number of records changed. A one-time <c>.bak</c> backup is created next
    /// to the file if one doesn't already exist.
    /// </summary>
    public static int ApplyFixes(TableReport table)
    {
        if (table.Failed || string.IsNullOrEmpty(table.FilePath))
        {
            return 0;
        }

        var edits = new List<(string StreamName, string Rule)>();
        foreach (Finding finding in table.Findings)
        {
            if (FixableRuleIds.Contains(finding.RuleId) && finding.Element is not null)
            {
                edits.Add((finding.Element.Id, finding.RuleId));
            }
        }

        if (edits.Count == 0)
        {
            return 0;
        }

        string bak = table.FilePath + ".bak";
        if (!File.Exists(bak))
        {
            File.Copy(table.FilePath, bak);
        }

        int changed = 0;
        using var root = RootStorage.Open(table.FilePath, FileMode.Open);
        Storage gameStg = root.OpenStorage("GameStg");

        foreach ((string streamName, string rule) in edits)
        {
            byte[] bytes;
            using (CfbStream s = gameStg.OpenStream(streamName))
            {
                bytes = new byte[s.Length];
                s.ReadExactly(bytes);
            }

            if (!ApplyEdit(rule, bytes))
            {
                continue;
            }

            using (CfbStream s = gameStg.OpenStream(streamName))
            {
                s.Position = 0;
                s.Write(bytes, 0, bytes.Length);
                s.SetLength(bytes.Length);
            }

            changed++;
        }

        return changed;
    }

    /// <summary>
    /// Mutates <paramref name="bytes"/> for a single rule. Returns false when the
    /// target record can't be found (nothing changed).
    /// </summary>
    private static bool ApplyEdit(string rule, byte[] bytes)
    {
        switch (rule)
        {
            case "ball-shadow-depth-mask":
                return WriteInt32Record(bytes, "ZMSK", 0);
            case "ball-shadow":
                return WriteInt32Record(bytes, "TMIN", -1);
            case "slingshot":
                return WriteInt32Record(bytes, "TMIN", 30);
            case "postitnote-alpha-mask":
                return WriteImageFloatTag(bytes, "ALTV"u8, 50f);
            default:
                return false;
        }
    }

    /// <summary>
    /// Finds a BIFF record by tag in a GameItem stream and overwrites its 4-byte
    /// Int32 payload. GameItem stream = [Int32 itemType][ [Int32 size][4 tag][data] ...].
    /// </summary>
    private static bool WriteInt32Record(byte[] bytes, string tag, int value)
    {
        int pos = 4; // skip leading Int32 itemType

        while (pos + 4 <= bytes.Length)
        {
            int size = BitConverter.ToInt32(bytes, pos);
            pos += 4;

            if (size < 4 || pos + 4 > bytes.Length)
            {
                break;
            }

            string recTag = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int dataLength = size - 4;
            int dataOffset = pos + 4;

            if (recTag == tag && dataOffset + 4 <= bytes.Length)
            {
                BitConverter.GetBytes(value).CopyTo(bytes, dataOffset);
                return true;
            }

            pos += 4 + dataLength;

            if (recTag == "ENDB")
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Overwrites the 4-byte float that follows a raw tag in an Image stream.
    /// Image streams embed a JPEG blob that breaks a sequential BIFF walk, so
    /// (matching <see cref="Model.VpxImage"/>) we scan for the tag directly.
    /// </summary>
    private static bool WriteImageFloatTag(byte[] bytes, ReadOnlySpan<byte> tag, float value)
    {
        for (int i = 0; i + tag.Length + 4 <= bytes.Length; i++)
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
                BitConverter.GetBytes(value).CopyTo(bytes, i + tag.Length);
                return true;
            }
        }

        return false;
    }
}
