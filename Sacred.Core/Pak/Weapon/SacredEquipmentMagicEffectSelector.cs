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
        // Only literal resolved codes participate. Generation group families in
        // Weapon.pak are inputs to item generation, not visual effect codes.
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

        return SacredEquipmentMagicEffectVariant.None;
    }

    /// <summary>Gold scans the base value block, then every merged record in vector
    /// order. Unlike stat aggregation, this scan does not filter by socket kind.</summary>
    public static SacredEquipmentMagicEffectVariant Select(in SacredEquipmentInstanceLayout values,
        IReadOnlyList<SacredEquipmentInstanceLayout> records)
    {
        var variant = Select(values.BonusTypes, values.BonusGroups, default);
        if (variant != SacredEquipmentMagicEffectVariant.None) return variant;
        foreach (var record in records)
        {
            variant = Select(record.BonusTypes, record.BonusGroups, default);
            if (variant != SacredEquipmentMagicEffectVariant.None) return variant;
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
