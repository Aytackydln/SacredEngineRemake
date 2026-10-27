using Sacred.Assets.Equipment;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Native unique-family selection after the prepared item selector.
/// Uses the caller's persistent shared values; it does not create an object.</summary>
public sealed class WorldEquipmentUniqueVariants
{
    private readonly SacredEquipmentGenerationData _data;
    private readonly WorldEquipmentSharedValues _shared;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<uint>> _families;

    public WorldEquipmentUniqueVariants(SacredEquipmentGenerationData data, WorldEquipmentSharedValues shared)
    {
        _data = data;
        _shared = shared;
        _families = SacredEquipmentModifierFamilies.Build(data.Templates.EquipmentRows.Select(row => row.ItemId),
            resource => data.Modifiers.Find(resource)?.Definition.WeaponType);
    }

    public uint? Select(uint preparedResource, ushort heroLevel, SacredRandomSequence random)
    {
        var modifier = _data.Modifiers.Find(preparedResource);
        if (modifier is null || !_families.TryGetValue(modifier.Definition.WeaponType, out var family)) return null;
        var rules = _data.CreatureDeathRewardRules ?? throw new InvalidOperationException("Original unique exclusion rules unavailable.");
        return SacredEquipmentUniqueVariants.Select(family, heroLevel, rules.UniqueExcludedMinimum, rules.UniqueExcludedMaximum, _shared.Candidate, random);
    }
}
