using System.Numerics;

namespace Sacred.Core.World.Portals;

/// <summary>A persistent effect created by a sector's Enter script. Position is the precise
/// world pivot from CreateObj 0x20, with tile coordinates retained separately.</summary>
public sealed record SacredPortalBillboard(int ScriptOffset, uint TypeId, Vector2 Position,
    int TileX, int TileY, byte SurfaceLevel, short HeightOffset);
