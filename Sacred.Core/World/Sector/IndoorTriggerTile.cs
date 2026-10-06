using Sacred.Core.World.Pathing;

namespace Sacred.Core.World.Sector;

/// <summary>A live trigger cell authored in an indoor WLDX tile grid.</summary>
public readonly record struct IndoorTriggerTile(int WorldX, int WorldY, WorldPathTile Pathing)
{
    public bool IsEntrance => Pathing.IsEntrance;
    public bool IsStairs => Pathing.Properties.SurfaceLevelDelta != 0;
    /// <summary>Signed destination displacement from WLDX tile bytes 0x1C/0x1D.</summary>
    public sbyte DestinationDeltaX { get; init; }
    public sbyte DestinationDeltaY { get; init; }
}
