using VPin.Inspector.Core.Model;

namespace VPin.Inspector.Platforms.Vpx.Model;

/// <summary>
/// VPX-specific table document. CONTAINS <see cref="VpxGameItem"/>s (composition)
/// and exposes the VBScript + resolved cGameName as the neutral base properties.
/// </summary>
public sealed class VpxTable : PinballTable
{
    public override string PlatformId => "vpx";

    public required IReadOnlyList<TableElement> ElementsList { get; init; }

    public override IReadOnlyList<TableElement> Elements => ElementsList;

    public override string? Script { get; init; }

    public override string? GameName { get; init; }
}
