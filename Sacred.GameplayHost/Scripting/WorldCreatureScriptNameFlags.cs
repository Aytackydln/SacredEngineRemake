namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48503B..0x48504F before final CalcResults: nonpositive signed
/// script-resource names set native +0x200 mask 0x20. Zero/no-name and the
/// NON_UNIQUE sentinel follow the same path. The remaining bit meaning is unknown.</summary>
/// <remarks>This is the isolated block. Its caller first requires interpreter
/// +0x12 (owned caller), clear +0x13 and non-aggressive input. Fresh non-owned
/// startup does not execute it, even for an unnamed creature.</remarks>
public static class WorldCreatureScriptNameFlags
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, uint resource)
        => unchecked((int)resource) <= 0 ? actor with { RuntimeFlags = actor.RuntimeFlags | 0x20 } : actor;
}
