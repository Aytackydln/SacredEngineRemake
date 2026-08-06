namespace Sacred.Core.World.Stairs;

/// <summary>One trigger cell and the zone anchor to which treppe.bin assigns it.</summary>
public readonly record struct WorldStairsCell(
    WorldStairsCoordinate Position,
    WorldStairsCoordinate Anchor);
