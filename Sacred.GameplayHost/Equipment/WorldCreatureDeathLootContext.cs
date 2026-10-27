using Sacred.Core.Loot;

namespace Sacred.GameplayHost.Equipment;

public sealed record WorldCreatureDeathLootSource(SacredCreatureDeathLootActor Actor, uint PhysicalFlags,
    ushort Level, ushort ExperienceMaximum, bool AdditionalDropBoost, bool Destroyed);

public sealed record WorldCreatureDeathLootHero(SacredCreatureDeathLootActor? Actor, SacredCreatureLootObserver? Observer,
    ushort Level, ushort EffectiveFind, uint SurvivalSeconds, ushort ChanceCurveSkillValue,
    byte Progression22, IReadOnlyList<uint> CompanionReferences);

/// <summary>Current authority/relation results and original actor/file inputs.
/// Hostility includes runtime team/link rules, not just default class hostility.</summary>
public sealed record WorldCreatureDeathLootContext(WorldCreatureDeathLootSource Source, WorldCreatureDeathLootHero Hero,
    SacredCreatureDeathLootActor? Killer, bool HostileToObserver, uint Difficulty, uint NearbyPartyMembers);
