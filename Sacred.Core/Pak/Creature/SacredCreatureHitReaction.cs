namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 535950/593770: a one-shot weighted control, independent of the main motion.</summary>
public static class SacredCreatureHitReaction
{
    public const uint Motion = 0xAF;
    public const float Speed = 3;
    public static float Weight(SacredCreatureClass creatureClass) => creatureClass == SacredCreatureClass.Hero ? .1f : .25f;
}
