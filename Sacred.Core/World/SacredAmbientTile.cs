using Sacred.Core.World.Elevation;

namespace Sacred.Core.World;

/// <summary>Gold 0x47C5B0 reads the liquid material and all four signed visual
/// offsets (WLDX +0x10..13), independently of physical terrain elevation.</summary>
public readonly record struct SacredAmbientTile(WldxTileProperties Properties, TerrainVisualElevationTile VisualElevation)
{
    public bool IsEligible(sbyte exclusiveMaximum, sbyte exclusiveMinimum)
    {
        var elevation = VisualElevation;
        return Properties.IsLiquid &&
            Between(elevation.SouthWest) && Between(elevation.NorthWest) &&
            Between(elevation.NorthEast) && Between(elevation.SouthEast);
        bool Between(sbyte value) => value < exclusiveMaximum && value > exclusiveMinimum;
    }
}
