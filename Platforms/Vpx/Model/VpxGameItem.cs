using VPin.Inspector.Core.Model;
using VPin.Inspector.Vpx; // reuse existing BiffReader

namespace VPin.Inspector.Platforms.Vpx.Model;

/// <summary>
/// Known VPX element (GameItem) type identifiers, matching the leading Int32
/// stored at the start of each GameItem stream.
/// </summary>
public enum VpxItemType
{
    Surface = 0,
    Flipper = 1,
    Timer = 2,
    Plunger = 3,
    TextBox = 4,
    Bumper = 5,
    Trigger = 6,
    Light = 7,
    Kicker = 8,
    Decal = 9,
    Gate = 10,
    Spinner = 11,
    Ramp = 12,
    Table = 13,
    LightCenter = 14,
    DragPoint = 15,
    Collection = 16,
    DispReel = 17,
    LightSeq = 18,
    Primitive = 19,
    Flasher = 20,
    Rubber = 21,
    HitTarget = 22,
}

/// <summary>
/// VPX-specific element. Inherits the neutral <see cref="TableElement"/> and
/// opts into the timer capability. This is the class that replaces the old
/// flat GameItem; the parsing logic is a straight port of GameItem.Parse.
/// </summary>
public sealed class VpxGameItem : TableElement, ITimerElement
{
    public required int RawType { get; init; }

    public override string TypeName =>
        Enum.IsDefined(typeof(VpxItemType), RawType)
            ? ((VpxItemType)RawType).ToString()
            : $"Unknown({RawType})";

    public bool TimerEnabled { get; init; }

    public int TimerIntervalMs { get; init; } = -1;

    /// <summary>
    /// Property-bag key for a primitive's "Hide parts behind" flag (BIFF
    /// <c>ZMSK</c> / <c>m_useDepthMask</c>). Stored as a <see cref="bool"/>.
    /// </summary>
    public const string HidePartsBehindKey = "HidePartsBehind";

    /// <summary>
    /// Ported from GameItem.Parse: reads NAME/TMON/TMIN from a GameItem stream.
    /// </summary>
    public static VpxGameItem Parse(string streamName, byte[] bytes)
    {
        var records = BiffReader.Read(bytes, out int itemType);

        string name = string.Empty;
        bool timerEnabled = false;
        int timerInterval = -1;
        bool? hidePartsBehind = null;

        foreach (var record in records)
        {
            switch (record.Tag)
            {
                case "NAME":
                    name = record.AsWideString();
                    break;
                case "TMON":
                    timerEnabled = record.AsBool();
                    break;
                case "TMIN":
                    timerInterval = record.AsInt32();
                    break;
                case "ZMSK":
                    hidePartsBehind = record.AsBool();
                    break;
            }
        }

        var properties = new Dictionary<string, object?>();
        if (hidePartsBehind is not null)
        {
            properties[HidePartsBehindKey] = hidePartsBehind.Value;
        }

        return new VpxGameItem
        {
            Id = streamName,
            Name = name,
            RawType = itemType,
            TimerEnabled = timerEnabled,
            TimerIntervalMs = timerInterval,
            Properties = properties,
        };
    }
}
