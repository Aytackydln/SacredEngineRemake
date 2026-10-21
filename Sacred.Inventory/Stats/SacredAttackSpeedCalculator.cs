using System;
using System.Collections.Generic;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Stats;

/// <summary>Gold on-foot hero attack speed: equipment bonus 811, weapon skill
/// speed bonuses (0x566070), weapon level penalty (0x57C6B5), then limits.
/// Weapon skills share the casting-speed curve at Balance.bin global +0xF4.</summary>
public sealed class SacredAttackSpeedCalculator(SacredSkillCurveLayout curve)
{
    public short Calculate(uint creatureType, uint objectFlags, uint valueFlags, int level,
        ReadOnlySpan<byte> skillOrder, SacredActorStats stats, ushort stance,
        SacredEquipment? left, SacredEquipment? right, int highestWeaponLevel)
    {
        var percent = unchecked((short)((creatureType == 5 ? 100 : 140) + (int)stats[SacredActorStat.AttackSpeed]));
        foreach (var skill in skillOrder)
        {
            if (!Applies(skill, stance, left, right)) continue;
            var rank = stats.Skills.GetValueOrDefault(skill);
            if (rank <= 0) continue;
            var bonus = (float)(2 * (curve.Limit - (double)curve.Base) *
                (1 - 1 / ((rank - 1d) / curve.Divisor + 1)) + curve.Base);
            percent = unchecked((short)(int)(percent + (double)bonus));
        }
        // Gold getSkillLevel clamps the penalty's weapon skill to a byte.
        var weaponSkill = stance == 8 ? (byte)6 : stance == 10 ? (byte)13 : stance == 0 ? (byte)24 :
            Matches(left, right, 0, 1, 2, 3, 4, 10) ? (byte)4 : Matches(left, right, 5, 6, 22) ? (byte)5 :
            Matches(left, right, 21, 23, 24) ? (byte)3 : stance is 7 or 12 or 13 or 14 ? (byte)7 : (byte)0;
        var lore = Math.Clamp(stats.Skills.GetValueOrDefault(weaponSkill), 0, byte.MaxValue);
        if (highestWeaponLevel > level && highestWeaponLevel > level + lore)
            percent = unchecked((short)(int)(percent * Math.Pow((level + lore + 9d) / (highestWeaponLevel + 9d), 2)));
        if ((objectFlags & 0x2000) != 0) percent = unchecked((short)(percent - 15));
        return (short)Math.Clamp(percent, creatureType == 5 ? 80 : 100, (valueFlags & 0x10000) != 0 ? 260 : 220);
    }

    private static bool Applies(byte skill, ushort stance, SacredEquipment? left, SacredEquipment? right) => skill switch
    {
        3 when stance != 8 && Matches(left, right, 21, 23, 24) => true,
        4 when stance != 8 && Matches(left, right, 0, 1, 2, 3, 4, 10) => true,
        5 when stance != 8 && Matches(left, right, 5, 6, 22) => true,
        6 when stance == 8 || stance == 0 && left is not null => true,
        7 when stance is 7 or 12 or 13 or 14 => true,
        13 when stance == 10 || left?.EquipmentType == (SacredEquipmentType)10 => true,
        24 when stance == 0 => true,
        _ => false
    };

    private static bool Matches(SacredEquipment? left, SacredEquipment? right, params ReadOnlySpan<byte> types)
    {
        foreach (var type in types)
            if (left is { } l && (byte)l.EquipmentType == type || right is { } r && (byte)r.EquipmentType == type) return true;
        return false;
    }
}
