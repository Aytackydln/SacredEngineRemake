namespace Sacred.GameplayHost.World;

/// <summary>Native action rules, separate from archive parsing and packet transport.</summary>
public static class GameplayCombatArtMotion
{
    // Gold EiSeraphim 0x506017: acquired BeeEffGee (0x3FA), motions[0x95],
    // fade .5, strength 1, rate 1. The clip itself comes from Models.tmp.
    public static uint? SpecialMove(ushort code, uint creatureType = 0) => code == 0x3FA
        ? creatureType == 0x8D ? 0x9AU : 0x95U : null;

    /// <summary>Grounded EiSeraphim entries at 0x505D30, 0x506017, 0x506137,
    /// 0x50619A and 0x506203. Ranged stances 7/12 cancel kick/rotation in the original.</summary>
    public static GameplayCombatArtAnimation? SelectSpecialMove(ushort code, uint creatureType, ushort stance)
    {
        // Gold 0x5500D0 routes only these resources to the Seraphim class action.
        if (creatureType is not (1 or 0x8D)) return GameplayHeroSpecialMoveMotion.Select(code, creatureType, stance);
        return code switch
        {
            0x3F5 or 0x41E when stance is not (7 or 12) => new(148, 1.6f),
            0x3FA => new(creatureType == 0x8D ? 154U : 149U, 1),
            0x3FB => new(creatureType == 0x8D ? 154U : 152U, 1),
            0x3FC when stance is not (7 or 12) => new(149, 1),
            0x3FD => new(stance is 2 or 3 ? 147U : 146U, 1),
            _ => null
        };
    }
}
