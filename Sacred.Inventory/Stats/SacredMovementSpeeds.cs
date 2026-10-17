using Sacred.Core.World;

namespace Sacred.Inventory.Stats;

/// <summary>Native integer world units per second and corresponding tile rates.</summary>
public readonly record struct SacredMovementSpeeds(short Percent, int Walk, int Run)
{
    public float WalkTilesPerSecond => Walk / SacredSectorCoordinates.WorldUnitsPerTile;
    public float RunTilesPerSecond => Run / SacredSectorCoordinates.WorldUnitsPerTile;
}
