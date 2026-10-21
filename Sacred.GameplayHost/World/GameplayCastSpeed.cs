namespace Sacred.GameplayHost.World;

/// <summary>Gold CalcResults 0x579AD7 and 0x57CA23: ordinary on-foot baseline,
/// before active effects and over-level weapon penalties. Learned skills do not write cast speed.</summary>
public static class GameplayCastSpeed
{
    public static short UnmodifiedPercent(uint creatureType, uint objectFlags)
    {
        var percent = creatureType == 5 ? 100 : 120;
        if ((objectFlags & 0x2000) != 0) percent -= 15;
        return (short)Math.Max(creatureType == 5 ? 80 : 100, percent);
    }
}
