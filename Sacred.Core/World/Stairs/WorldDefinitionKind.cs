namespace Sacred.Core.World.Stairs;

/// <summary>Native eDefTypes, stored as a 32-bit discriminator in DefStru.</summary>
public enum WorldDefinitionKind : int
{
    /// <summary>Def_Position.</summary>
    Position = 0,
    /// <summary>Def_Num.</summary>
    Number = 1,
    /// <summary>Def_PoolPos.</summary>
    PoolPosition = 2,
    /// <summary>Def_PoolRgn.</summary>
    PoolRegion = 3,
}
