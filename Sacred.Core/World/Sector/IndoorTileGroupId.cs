namespace Sacred.Core.World.Sector;

/// <summary>Stable identity of an indoor tile grid within its owning WLDX sector.</summary>
public readonly record struct IndoorTileGroupId(SectorCoord OwnerSector, int GroupIndex);
