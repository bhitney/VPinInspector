using OpenMcdf;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Core.Rules;

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// Applies a small, fixed set of in-place corrections to a .vpx file for the
/// findings this tool can safely auto-fix. Only a handful of rules are
/// supported; every other finding is left for the user to correct by hand in
/// VPX.
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
        "ball-shadow-depth-mask",    // primitive ZMSK -> 0 (uncheck "Hide parts behind")
        "flipper-shadow-depth-mask", // primitive ZMSK -> 0 (uncheck "Hide parts behind")
        "high-score-tape",           // primitive ZMSK/STRE/REEN -> 0, ISTO -> 1
        "configurable-shadow",       // primitive ZMSK -> 0 (uncheck "Hide parts behind")
        "ball-shadow",               // timer TMIN -> -1
        "graphics-update-timer",     // timer TMIN -> -1
        "slingshot",                 // surface timer TMIN -> 30
        "rolling",                   // timer TMIN -> suggest (user preference, e.g. -1 or 10)
        "postitnote-alpha-mask",     // image ALTV -> 50
        "ball-shadow-alpha-mask",    // image ALTV -> 1
    };

    /// <summary>True when the table has at least one finding this writer can fix.</summary>
    public static bool HasFixableFinding(TableReport table) =>
        !table.Failed && table.Findings.Any(f => FixableRuleIds.Contains(f.RuleId));

    /// <summary>
    /// The outcome of an <see cref="ApplyFixes"/> run. <see cref="Attempted"/> is
    /// the number of fixable findings the writer tried to correct; <see cref="Changed"/>
    /// is how many actually resulted in a record edit. When <c>Changed &lt; Attempted</c>
    /// some fixes silently no-op'd (the target BIFF record wasn't found), and the
    /// affected findings will legitimately re-appear on the next scan.
    /// <see cref="UnchangedRuleIds"/> lists those rule ids.
    /// </summary>
    public readonly record struct FixResult(int Attempted, int Changed, IReadOnlyList<string> UnchangedRuleIds)
    {
        /// <summary>True when every attempted fix produced a record edit.</summary>
        public bool AllApplied => Attempted > 0 && Changed == Attempted;

        /// <summary>True when at least one attempted fix silently did nothing.</summary>
        public bool HasPartialFailure => Changed < Attempted;
    }

    /// <summary>
    /// Applies every fixable finding for the given table in place. Returns a
    /// <see cref="FixResult"/> describing how many fixes were attempted vs.
    /// actually applied. A one-time <c>.bak</c> backup is created next to the
    /// file if one doesn't already exist.
    /// </summary>
    public static FixResult ApplyFixes(TableReport table)
    {
        if (table.Failed || string.IsNullOrEmpty(table.FilePath))
        {
            return new FixResult(0, 0, Array.Empty<string>());
        }

        var edits = new List<(string StreamName, string Rule, int? Suggest)>();
        foreach (Finding finding in table.Findings)
        {
            if (FixableRuleIds.Contains(finding.RuleId) && finding.Element is not null)
            {
                edits.Add((finding.Element.Id, finding.RuleId, TryGetSuggest(finding)));
            }
        }

        if (edits.Count == 0)
        {
            return new FixResult(0, 0, Array.Empty<string>());
        }

        string bak = table.FilePath + ".bak";
        if (!File.Exists(bak))
        {
            File.Copy(table.FilePath, bak);
        }

        int changed = 0;
        var unchanged = new List<string>();
        using var root = RootStorage.Open(table.FilePath, FileMode.Open);
        Storage gameStg = root.OpenStorage("GameStg");

        foreach ((string streamName, string rule, int? suggest) in edits)
        {
            byte[] bytes;
            using (CfbStream s = gameStg.OpenStream(streamName))
            {
                bytes = new byte[s.Length];
                s.ReadExactly(bytes);
            }

            if (!ApplyEdit(rule, bytes, suggest))
            {
                unchanged.Add(rule);
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

        return new FixResult(edits.Count, changed, unchanged);
    }

    /// <summary>
    /// Reads the optional integer <c>suggest</c> value a declarative rule may
    /// attach to a finding (see <c>DeclarativeElementRule</c>). Returns null when
    /// absent or unparsable.
    /// </summary>
    private static int? TryGetSuggest(Finding finding)
    {
        if (finding.Details is { } details &&
            details.TryGetValue("suggest", out string? raw) &&
            int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// Mutates <paramref name="bytes"/> for a single rule. Returns false when the
    /// target record can't be found (nothing changed). <paramref name="suggest"/>
    /// carries the rule's authored recommendation (rules.json <c>suggest</c>)
    /// when available, so value-driven fixes write exactly what the rule advises.
    /// </summary>
    private static bool ApplyEdit(string rule, byte[] bytes, int? suggest)
    {
        switch (rule)
        {
            case "ball-shadow-depth-mask":
                return WriteInt32Record(bytes, "ZMSK", 0);
            case "flipper-shadow-depth-mask":
                return WriteInt32Record(bytes, "ZMSK", 0);
            case "configurable-shadow":
                return WriteInt32Record(bytes, "ZMSK", 0);
            case "high-score-tape":
            {
                // Correct all four properties; a change to any one counts.
                bool changed = WriteInt32Record(bytes, "ZMSK", 0);   // Hide parts behind -> unchecked
                changed |= WriteInt32Record(bytes, "STRE", 0);       // Static Rendering -> unchecked
                changed |= WriteInt32Record(bytes, "REEN", 0);       // Reflection Enabled -> unchecked
                changed |= WriteInt32Record(bytes, "ISTO", 1);       // Toy (never collidable) -> checked
                return changed;
            }
            case "ball-shadow":
                return WriteInt32Record(bytes, "TMIN", -1);
            case "graphics-update-timer":
                return WriteInt32Record(bytes, "TMIN", -1);
            case "slingshot":
                // Follow the rule's authored suggestion when present; fall back
                // to 30 for older rule sets that don't carry a suggest value.
                return WriteInt32Record(bytes, "TMIN", suggest ?? 30);
            case "rolling":
                // Fully user-configurable via the rule's suggest value (e.g. -1
                // for per-frame or 10ms). Fall back to -1 when unspecified.
                return WriteInt32Record(bytes, "TMIN", suggest ?? -1);
            case "postitnote-alpha-mask":
                return WriteImageFloatTag(bytes, "ALTV"u8, 50f);
            case "ball-shadow-alpha-mask":
                return WriteImageFloatTag(bytes, "ALTV"u8, 1f);
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
