namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Gold 0x5C2BF0: elemental dominance for generated name keys.</summary>
public static class SacredEquipmentNameElement
{
    public static uint Calculate(in SacredEquipmentInstanceLayout values, float levelFactorHundred)
    {
        var damage = values.Damage;
        if (Dominant(unchecked((short)damage.PhysicalDamageMaximum), unchecked((short)damage.FireDamageMaximum),
            unchecked((short)damage.MagicDamageMaximum), unchecked((short)damage.PoisonDamageMaximum)) is { } attack) return attack;
        if (Dominant(values.PhysicalProtection, values.FireProtection, values.MagicProtection, values.PoisonProtection) is { } armor) return armor;
        for (var i = 0; i < 8; i++)
        {
            var code = (ushort)values.BonusGroups[i];
            var flag = code switch { 802 or 806 => 0x400, 803 or 807 => 0x800, 804 or 808 => 0x1000, _ => 0 };
            if (flag != 0) return unchecked((uint)(flag + values.BonusValues[i] * 2));
        }
        var total = unchecked((short)damage.PhysicalDamageMaximum) + unchecked((short)damage.FireDamageMaximum)
            + unchecked((short)damage.MagicDamageMaximum) + unchecked((short)damage.PoisonDamageMaximum)
            + values.PhysicalProtection + values.FireProtection + values.MagicProtection + values.PoisonProtection;
        return unchecked((uint)(long)(total / (double)levelFactorHundred));
    }

    private static uint? Dominant(short physical, short fire, short magic, short poison)
    {
        var total = physical + fire + magic + poison;
        if (fire != 0 && fire > magic && fire > poison) return unchecked((uint)(fire * 100 / total + 0x400));
        if (magic != 0 && magic > fire && magic > poison) return unchecked((uint)(magic * 100 / total + 0x800));
        if (poison != 0 && poison > fire && poison > magic) return unchecked((uint)(poison * 100 / total + 0x1000));
        return null;
    }
}
