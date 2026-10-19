namespace Sacred.GameplayHost.World;

/// <summary>Native action rules, separate from archive parsing and packet transport.</summary>
public static class GameplayCombatArtMotion
{
    // Gold EiSeraphim 0x506017: acquired BeeEffGee (0x3FA), motions[0x95],
    // fade .5, strength 1, rate 1. The clip itself comes from Models.tmp.
    public static uint? SpecialMove(ushort code, uint creatureType = 0) => code == 0x3FA
        ? creatureType == 0x8D ? 0x9AU : 0x95U : null;
}
