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
    /// Property-bag key for a primitive's "Static Rendering" flag (BIFF
    /// <c>STRE</c> / <c>m_staticRendering</c>). Stored as a <see cref="bool"/>.
    /// </summary>
    public const string StaticRenderingKey = "StaticRendering";

    /// <summary>
    /// Property-bag key for a primitive's "Reflection Enabled" flag (BIFF
    /// <c>REEN</c> / <c>m_reflectionEnabled</c>). Stored as a <see cref="bool"/>.
    /// </summary>
    public const string ReflectionEnabledKey = "ReflectionEnabled";

    /// <summary>
    /// Property-bag key for a primitive's "Toy" flag (BIFF <c>ISTO</c> /
    /// <c>m_toy</c>). When true the primitive is a toy (never collidable);
    /// when false it is collidable. Stored as a <see cref="bool"/>.
    /// </summary>
    public const string ToyKey = "Toy";

    /// <summary>
    /// Property-bag key for the set of image NAMEs this element references.
    /// Stored as an <see cref="IReadOnlyList{String}"/> of image names (the
    /// values of the element's image-slot BIFF records). Empty or absent when
    /// the element references no image. Used by the image-usage rule to decide
    /// which images are referenced by the object tree (VPX's "In Use" idea).
    /// </summary>
    public const string ImageReferencesKey = "ImageReferences";

    /// <summary>
    /// The BIFF tags whose string value is an image NAME, verified against real
    /// tables (Silver Cup, The Shadow): <c>IMAG</c> is the common image slot
    /// (surfaces, primitives, ramps, flippers, plungers, rubbers, targets,
    /// flashers, dispreels); <c>SIMG</c> is a surface's side image; <c>IMG1</c>
    /// is a light's image; <c>IMGW</c> is a ramp's wall image; <c>IMAB</c> is a
    /// flasher's second image (Image B); <c>NRMA</c> is a primitive's normal map
    /// image. Material (<c>MATR</c>/<c>TOMA</c>) and texture (<c>ATEX</c>/
    /// <c>TEXC</c>) tags are deliberately excluded: they are not image-name
    /// references.
    /// </summary>
    private static readonly string[] ImageSlotTags =
        { "IMAG", "SIMG", "IMG1", "IMGW", "IMAB", "NRMA" };

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
        bool? staticRendering = null;
        bool? reflectionEnabled = null;
        bool? toy = null;
        List<string>? imageReferences = null;

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
                case "STRE":
                    staticRendering = record.AsBool();
                    break;
                case "REEN":
                    reflectionEnabled = record.AsBool();
                    break;
                case "ISTO":
                    toy = record.AsBool();
                    break;
                default:
                    if (Array.IndexOf(ImageSlotTags, record.Tag) >= 0)
                    {
                        // Image-slot values are single-byte (Latin1) strings,
                        // like image NAMEs - not UTF-16 like a GameItem NAME.
                        string imageName = record.AsLatin1String();
                        if (!string.IsNullOrEmpty(imageName))
                        {
                            (imageReferences ??= new List<string>()).Add(imageName);
                        }
                    }
                    break;
            }
        }

        var properties = new Dictionary<string, object?>();
        if (hidePartsBehind is not null)
        {
            properties[HidePartsBehindKey] = hidePartsBehind.Value;
        }
        if (staticRendering is not null)
        {
            properties[StaticRenderingKey] = staticRendering.Value;
        }
        if (reflectionEnabled is not null)
        {
            properties[ReflectionEnabledKey] = reflectionEnabled.Value;
        }
        if (toy is not null)
        {
            properties[ToyKey] = toy.Value;
        }
        if (imageReferences is not null)
        {
            properties[ImageReferencesKey] = (IReadOnlyList<string>)imageReferences;
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
