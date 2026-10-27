namespace Sacred.Core.Pak.Weapon;

/// <summary>Live selection's temporary-object services. Null/false means an
/// unresolved dependency; a zero created reference is the native allocation
/// failure and still consumes one of the three attempts. Effects share RNG.</summary>
public sealed record SacredEquipmentFitEffects(Func<uint, uint?> Create,
    Func<uint, bool?> Fits, Func<uint, bool> Destroy);
public enum SacredEquipmentFitPending { None, Creation, Fit, Destruction }
public readonly record struct SacredEquipmentSelectionResult(uint? Resource, SacredEquipmentFitPending Pending = SacredEquipmentFitPending.None,
    uint TemporaryReference = 0)
{
    public bool Completed => Pending == SacredEquipmentFitPending.None;
}

/// <summary>Creature-resource aliases in the original final preference branch.</summary>
public sealed record SacredEquipmentPreferenceRules(IReadOnlyDictionary<uint, uint> ResourceMasks);
