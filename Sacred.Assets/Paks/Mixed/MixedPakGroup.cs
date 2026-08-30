namespace Sacred.Assets.Paks.Mixed;

/// <summary>A composed Mixed.pak sprite and its authored world-placement point.</summary>
public sealed record MixedPakGroup(
    ushort PlacementX,
    ushort PlacementY,
    IReadOnlyList<MixedCutoutRecord> Pieces);
