using Sacred.Core.World.Pathing;

namespace Sacred.Core.World.Sector;

/// <summary>A live trigger cell authored in an indoor WLDX tile grid.</summary>
public readonly record struct IndoorTriggerTile(int WorldX, int WorldY, WorldPathTile Pathing)
{
    // Path type 9 is used by the entrance cells verified against Sacred's building doors.
    public bool IsEntrance => Pathing.Type == 9;
}
