namespace Sacred.GameplayHost.World;

/// <summary>EiBow's ordinary release timing. Special combat-art projectile factories remain separate.</summary>
public static class GameplayBowRelease
{
    public static float Progress(float tailFraction)
    {
        var value = 1 - tailFraction;
        return value <= 0 ? .5f : value;
    }
    // Gold 0x4DEA2B..0x4DEDE2 chooses these native types, then loads their Items.pak definitions.
    // This is executable gameplay policy, never an item-name or texture exception in asset readers.
    public static uint ProjectileType(byte combatArt) => combatArt switch
    {
        4 => 3078,
        6 => 3074,
        7 => 3075,
        8 => 3077,
        0x3D => 3069,
        0x3E => 3068,
        0x3F => 3079,
        _ => 3070
    };
    public static float Spread(byte combatArt, int index, int count)
    {
        var step = combatArt switch { 4 or 6 or 7 => .15f, 8 => MathF.Tau / count, 0x3F => .01f, _ => .05f };
        return (index == 0 ? 0 : (index + 1) / 2 * (index % 2 == 0 ? -1 : 1)) * step;
    }
}
