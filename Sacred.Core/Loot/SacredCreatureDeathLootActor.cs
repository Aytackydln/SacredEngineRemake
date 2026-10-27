using Sacred.Core.Pak.Creature;

namespace Sacred.Core.Loot;

/// <summary>Resolved Gold actor fields used by the death loot gate. Resource
/// class comes from Creature.pak, separately from the runtime relation class.</summary>
public readonly record struct SacredCreatureDeathLootActor(uint Reference, uint Resource,
    uint RelationClass, uint Behavior, int OwnerReference, int MaximumHitPoints, SacredCreatureClass ResourceClass);

public readonly record struct SacredCreatureLootObserver(ushort KiState, ushort EiState, int CurrentHitPoints)
{
    /// <summary>Gold 0x549000. Hostility must include the current native relation
    /// table, links and team rules, rather than assuming default class hostility.</summary>
    public bool CanReceiveLoot(bool hostileToSource) => KiState != 9 && EiState != 6 && CurrentHitPoints != 0 && hostileToSource;
}
