using Sacred.Core.Loot;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Null means unresolved. A zero resource/reference is a resolved
/// native empty result. Creation reports the original dynamic-type/value probes.</summary>
public readonly record struct WorldEquipmentRewardObject(uint Reference, bool Physical, bool Item, uint? ValuesResource);
public sealed record WorldEquipmentRewardEffects(Func<uint, bool> Money,
    Func<uint> HeroResource, Func<SacredEquipmentRewardQuery, uint?> Select,
    Func<uint, WorldEquipmentRewardObject?> Create,
    Func<WorldEquipmentRewardObject, SacredEquipmentRewardGeneration, bool> Normalize,
    Func<WorldEquipmentRewardObject, bool> PrepareLaunch,
    Func<WorldEquipmentRewardObject, SacredEquipmentRewardLaunch, bool> Launch);

public enum WorldEquipmentRewardPending { None, Money, Selection, Creation, Normalization, PrepareLaunch, Launch }
public readonly record struct WorldEquipmentRewardResult(WorldEquipmentRewardPending Pending)
{
    public bool Completed => Pending == WorldEquipmentRewardPending.None;
}
