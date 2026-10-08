namespace Sacred.Core.Pak.Weapon.Bonuses;

/// <summary>Interprets native BonusG/BonusT/BonusP slots without loading localized resources.</summary>
public static class SacredEquipmentBonusDecoder
{
    public static IReadOnlyList<SacredEquipmentBonus> Decode(SacredEquipment equipment)
    {
        var groups = equipment.BonusGroups;
        ReadOnlySpan<uint> bonusGroups = groups;
        var bonuses = new List<SacredEquipmentBonus>(bonusGroups.Length);
        for (var index = 0; index < bonusGroups.Length; index++)
        {
            var group = bonusGroups[index];
            var type = equipment.BonusTypes[index];
            if (group != 0 || type != 0)
                bonuses.Add(Decode(group, type, equipment.BonusValues[index]));
        }

        return bonuses;
    }

    public static SacredEquipmentBonus Decode(uint group, ushort type, short value)
    {
        var code = (ushort)group;
        var attribute = (byte)(group >> 16);
        var kind = Kind(code, type);
        var unit = Unit(code, kind, attribute);
        var element = code is >= 801 and <= 808 ? (SacredEquipmentElement)((code - 801) % 4) : default;
        var damageKind = (type & 0x4000) != 0 ? SacredEquipmentDamageKind.Weapon
            : (type & 0x2000) != 0 ? SacredEquipmentDamageKind.Spell : SacredEquipmentDamageKind.All;

        // Gold 0x568EF0 reads this byte for runtime values even when the description
        // switch omits an attribute suffix. Preserve the archive selector on every code.
        return new(code, value, kind, unit, attribute, (byte)((type >> 8) & 15), (byte)type, element, damageKind);
    }

    private static SacredEquipmentBonusKind Kind(ushort code, ushort type)
    {
        if (code is >= 801 and <= 808)
        {
            if ((type & 0x8000) != 0)
                return SacredEquipmentBonusKind.Resistance;
            return code <= 804 ? SacredEquipmentBonusKind.DamageBonus : SacredEquipmentBonusKind.Damage;
        }

        if (code == 809)
            return SacredEquipmentBonusKind.Attack;
        if (code == 810)
            return SacredEquipmentBonusKind.Defense;
        if (code is >= 600 and < 799)
            return SacredEquipmentBonusKind.Skill;
        if (SacredEquipmentBonusResources.Get(code) is not null)
            return SacredEquipmentBonusKind.Named;
        if (SacredCombatArtResources.GetNameResource(code) is not null)
            return SacredEquipmentBonusKind.CombatArt;

        return SacredEquipmentBonusKind.Unknown;
    }

    private static SacredEquipmentBonusUnit Unit(ushort code, SacredEquipmentBonusKind kind, byte attribute)
    {
        if (kind is SacredEquipmentBonusKind.Resistance or SacredEquipmentBonusKind.Damage
            or SacredEquipmentBonusKind.Attack or SacredEquipmentBonusKind.Defense)
            return SacredEquipmentBonusUnit.Percent;
        if (kind == SacredEquipmentBonusKind.DamageBonus && attribute is >= 1 and <= 6)
            return SacredEquipmentBonusUnit.Percent;
        if (kind == SacredEquipmentBonusKind.Unknown)
            return SacredEquipmentBonusUnit.None;
        if (kind == SacredEquipmentBonusKind.Named && SacredEquipmentBonusResources.Get(code) is { } definition)
        {
            if (!definition.HasValue)
                return SacredEquipmentBonusUnit.None;
            return definition.Percent ? SacredEquipmentBonusUnit.Percent : SacredEquipmentBonusUnit.Points;
        }

        return SacredEquipmentBonusUnit.Points;
    }
}
