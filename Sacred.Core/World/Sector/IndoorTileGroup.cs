using Sacred.Core.World.Pathing;

namespace Sacred.Core.World.Sector;

/// <summary>
/// An independently-authored WLDX membership/navigation grid for a building floor.
/// Its bounds and floor ordinal select the matching static-object render layer.
/// </summary>
public sealed class IndoorTileGroup(
    IndoorTileGroupId id,
    int worldX,
    int worldY,
    int width,
    int height,
    uint kind,
    byte surfaceLevel,
    WorldPathingLayer pathing,
    IndoorTilePresenceLayer presence,
    IReadOnlyList<IndoorTriggerTile> triggers)
{
    public IndoorTileGroupId Id { get; } = id;
    public int WorldX { get; } = worldX;
    public int WorldY { get; } = worldY;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public uint Kind { get; } = kind;
    public byte SurfaceLevel { get; } = surfaceLevel;
    public int SurfaceRenderLayer { get; } = 1 << surfaceLevel;
    public WorldPathingLayer Pathing { get; } = pathing;
    public IndoorTilePresenceLayer Presence { get; } = presence;
    public IReadOnlyList<IndoorTriggerTile> Triggers { get; } = triggers;
    public IEnumerable<IndoorTriggerTile> Entrances => Triggers.Where(static trigger => trigger.IsEntrance);

    public bool ContainsWorldTile(int worldX, int worldY) =>
        worldX >= WorldX && worldX < WorldX + Width &&
        worldY >= WorldY && worldY < WorldY + Height;

    public bool TryGetAuthoredLocalTile(int worldX, int worldY, out int localX, out int localY)
    {
        localX = worldX - WorldX;
        localY = worldY - WorldY;
        return (uint)localX < (uint)Width &&
               (uint)localY < (uint)Height &&
               Presence[localX, localY];
    }
}
