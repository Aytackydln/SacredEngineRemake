using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>The actor predicates and record identity resolved by the equipment
/// traversal. Main and enabled merged records are processed in native order.</summary>
public readonly record struct SacredCreatureEquipmentContext(uint ActorResource, bool Unarmed,
    bool MergedRecord, int LearnedCombatArtCount, SacredCreatureCombatProfileLayout Profile);
