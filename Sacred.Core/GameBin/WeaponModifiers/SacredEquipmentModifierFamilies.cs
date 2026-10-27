namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>Gold 0x5D4AC0 builds the unique-variant map in Weapon.pak record
/// order, including duplicate resources, keyed by each resolved wpmod WeaponType.</summary>
public static class SacredEquipmentModifierFamilies
{
    public static IReadOnlyDictionary<int, IReadOnlyList<uint>> Build(IEnumerable<uint> resourceOrder, Func<uint, int?> modifierType)
    {
        var families = new Dictionary<int, List<uint>>();
        foreach (var resource in resourceOrder)
        {
            if (modifierType(resource) is not { } key) continue;
            if (!families.TryGetValue(key, out var family)) families[key] = family = [];
            family.Add(resource);
        }
        return families.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<uint>)pair.Value.AsReadOnly());
    }
}
