namespace Sacred.Core.World;

/// <summary>
/// Footstep subset of native eObjectSound. Values are sound-profile slots, not terrain codes.
/// Demo cMSS::playFootstep at 0x60FDD1 and Gold at 0x68EDDA have matching dispatch tables.
/// </summary>
public enum TerrainFootstepKind : ushort
{
    /// <summary>SND_FOOTSTEP_GRASS.</summary>
    Grass = 1,
    /// <summary>SND_FOOTSTEP_WATER.</summary>
    Water = 2,
    /// <summary>SND_FOOTSTEP_SNOW.</summary>
    Snow = 3,
    /// <summary>SND_FOOTSTEP_SWAMP.</summary>
    Swamp = 4,
    /// <summary>SND_FOOTSTEP_STONE.</summary>
    Stone = 5,
    /// <summary>SND_FOOTSTEP_SAND.</summary>
    Sand = 6,
    /// <summary>SND_FOOTSTEP_WOOD.</summary>
    Wood = 7,
}
