namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x461F83..0x461FA2. Selectors above zero set +0x2B7 bit 3;
/// zero clears it and the omitted signed sentinel preserves it. Result recomputation
/// and startup notification suppression belong to the SetNPCState caller.</summary>
public static class WorldCreatureScriptFlagState
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, int selector) => actor with
    {
        Flags2B7 = selector == 0 ? (ushort)(actor.Flags2B7 & ~8)
            : selector > 0 ? (ushort)(actor.Flags2B7 | 8) : actor.Flags2B7
    };
}
