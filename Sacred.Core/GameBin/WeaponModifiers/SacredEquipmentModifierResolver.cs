namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>Resource ranges extracted from original modifier-lookup instructions.
/// Source bounds are exclusive; rejected redirected-target bounds are inclusive.</summary>
public readonly record struct SacredEquipmentModifierRedirectRules(uint FirstMinimum, uint FirstMaximum,
    uint SecondMinimum, uint SecondMaximum)
{
    public bool RedirectSource(uint type) => type > FirstMinimum && type < FirstMaximum || type > SecondMinimum && type < SecondMaximum;
    public bool RejectTarget(uint type) => type >= FirstMinimum && type <= FirstMaximum || type >= SecondMinimum && type <= SecondMaximum;
}

/// <summary>Gold 0x42CA70 modifier association: optional single base-visual redirect,
/// then the first wpmod row containing that resource. This does not recursively
/// traverse visuals or use the last matching definition.</summary>
public sealed class SacredEquipmentModifierResolver
{
    private readonly Dictionary<uint, SacredWeaponModifier> _first = new();
    private readonly IReadOnlyDictionary<uint, uint> _baseItems;
    private readonly SacredEquipmentModifierRedirectRules _rules;

    public SacredEquipmentModifierResolver(IEnumerable<SacredWeaponModifier> records,
        IReadOnlyDictionary<uint, uint> baseItems, SacredEquipmentModifierRedirectRules rules)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(baseItems);
        _baseItems = baseItems;
        _rules = rules;
        foreach (var record in records)
            foreach (var type in record.ItemIds) _first.TryAdd(type, record);
    }

    public SacredWeaponModifier? Find(uint type)
    {
        if (_baseItems.TryGetValue(type, out var basis) && _rules.RedirectSource(type))
        {
            type = basis;
            if (_rules.RejectTarget(type)) return null;
        }
        return _first.GetValueOrDefault(type);
    }
}
