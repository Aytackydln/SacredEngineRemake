namespace Sacred.GameplayHost.Scripting;

/// <summary>Explicit runtime inputs to native SetLevel. Reference is the actor's object
/// identity at the time of the call, not its owner slot. Constructor calls use zero.</summary>
public readonly record struct WorldCreatureLevelContext(uint ObjectReference, uint Difficulty, bool Multiplayer,
    uint BehaviorFlags, byte ActorFlags2B8, ushort SectorMaximumLevel, uint RuntimeLevelAddition,
    bool SkipDifficultyAdjustment = false, bool IgnoreLimits = false, uint? TypeMaximumOverride = null);
