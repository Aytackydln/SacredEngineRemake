namespace Sacred.Core.Pak.Weapon;

/// <summary>
/// Reproduces Sacred.exe 0x5CEA50, which chooses the colour variant for the
/// TYPE_FX_FIREBALL system attached to <c>weapon_gl01</c>. The selector consumes
/// resolved item bonus codes; it is independent of elemental damage values.
/// </summary>
public static class SacredEquipmentMagicEffectSelector
{
    // 0x5CEBA8 maps bonus codes 1..51 to the return blocks at 0x5CEB23..0x5CEB78.
    private static ReadOnlySpan<byte> VariantByBonusCode =>
    [
        3, 2, 5, 4, 1, 0, 5, 5, 5, 5, 1, 0, 5, 0, 5, 2, 5,
        5, 5, 5, 5, 5, 1, 1, 2, 2, 4, 3, 3, 5, 5, 5, 3, 4,
        4, 5, 5, 5, 5, 5, 0, 5, 5, 0, 0, 0, 0, 0, 0, 0, 5
    ];

    public static SacredEquipmentMagicEffectVariant Select(
        in SacredEquipmentBonusTypes bonusTypes,
        in SacredEquipmentBonusGroups bonusGroups,
        SacredEquipmentType equipmentType)
    {
        // Prefer literal resolved codes already present in the prototype. Runtime
        // modifier rows use this representation when 0x5CEA50 scans them.
        for (var index = 0; index < 8; index++)
        {
            var group = bonusGroups[index];
            if (bonusTypes[index] == 0 && group == 0)
                continue;

            var code = (ushort)group;
            if (code is < 1 or > 51)
                continue;

            var variant = VariantByBonusCode[code - 1];
            if (variant != 0)
                return (SacredEquipmentMagicEffectVariant)variant;
        }

        // Mage-staff prototypes encode their preview effect behind a generation
        // family (6xx, 8xx, 10xx). Other equipment can use the same group numbers
        // for unrelated generated bonuses; treating every group suffix as a
        // resolved runtime code incorrectly enables FIREBALL on ordinary weapons.
        if (equipmentType != SacredEquipmentType.MageStaff)
            return SacredEquipmentMagicEffectVariant.None;

        for (var index = 0; index < 8; index++)
        {
            var encoded = (ushort)bonusGroups[index];
            var family = encoded / 100;
            if (family is not (6 or 8 or 10))
                continue;

            var code = encoded % 100;
            if (code is < 1 or > 51)
                continue;

            var variant = VariantByBonusCode[code - 1];
            if (variant != 0)
                return (SacredEquipmentMagicEffectVariant)variant;
        }

        return SacredEquipmentMagicEffectVariant.None;
    }
}

public enum SacredEquipmentMagicEffectVariant : byte
{
    None,
    Red,
    Green,
    Neutral,
    Blue,
    Violet
}
