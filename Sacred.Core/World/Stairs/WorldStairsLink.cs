namespace Sacred.Core.World.Stairs;

public readonly record struct WorldStairsDestination(
    float X,
    float Y);

public sealed record WorldStairsLink(
    string Name,
    WorldStairsZone SourceZone,
    WorldStairsZone TargetZone,
    WorldStairsDestination Destination);
