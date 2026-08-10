namespace VPX_Inspector.Vpx;

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
/// A parsed VPX element with the fields relevant to timer inspection.
/// </summary>
public sealed class GameItem
{
    public required string StreamName { get; init; }

    public required int RawType { get; init; }

    public string TypeName =>
        Enum.IsDefined(typeof(VpxItemType), RawType)
            ? ((VpxItemType)RawType).ToString()
            : $"Unknown({RawType})";

    public string Name { get; init; } = string.Empty;

    /// <summary>Whether the element's built-in timer is enabled (TMON).</summary>
    public bool TimerEnabled { get; init; }

    /// <summary>The element's timer interval in milliseconds (TMIN). -1 when absent.</summary>
    public int TimerIntervalMs { get; init; } = -1;

    public bool HasTimer => TimerIntervalMs >= 0;

    public static GameItem Parse(string streamName, byte[] bytes)
    {
        var records = BiffReader.Read(bytes, out int itemType);

        string name = string.Empty;
        bool timerEnabled = false;
        int timerInterval = -1;

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
            }
        }

        return new GameItem
        {
            StreamName = streamName,
            RawType = itemType,
            Name = name,
            TimerEnabled = timerEnabled,
            TimerIntervalMs = timerInterval,
        };
    }
}
