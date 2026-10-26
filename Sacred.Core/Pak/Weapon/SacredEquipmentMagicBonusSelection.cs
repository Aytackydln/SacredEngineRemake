using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Gold 0x5D1750, choosing a concrete bonus for one of nine families.</summary>
public static class SacredEquipmentMagicBonusSelection
{
    public static bool Apply(int kind, Span<byte> values, int slot, bool jewelry, uint budget,
        SacredItemCategory category, byte usage, IReadOnlyList<SacredEquipmentMagicPoolLayout> ranges,
        SacredEquipmentBonusBalance curves, SacredRandomSequence random, Func<uint, byte> combatArtClass)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(values[(58 + slot * 2)..], 0);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(values[4..]);
        var allClasses = (flags & 0xC0003F) == 0xC0003F;
        var weaponType = (flags >> 8) & 255;
        var level = values[25];
        bool Affordable(uint code) => SacredEquipmentMagicBonusValue.CanAfford(code, budget, jewelry, level, curves);
        var candidates = new List<uint>();
        if (kind is 0 or 1)
        {
            var hasProtection = 0;
            for (var i = 0; i < 4; i++)
            {
                var damage = BinaryPrimitives.ReadInt16LittleEndian(values[(36 + i * 2)..]);
                var protection = BinaryPrimitives.ReadInt16LittleEndian(values[(50 + i * 2)..]);
                hasProtection += protection;
                if (damage != 0 || protection != 0) candidates.Add((uint)(801 + kind * 4 + i));
            }
            var accepted = false;
            while (candidates.Count != 0)
            {
                var code = EraseThenRead(candidates, random);
                if (kind == 0)
                {
                    var percentage = random.Next() % 6 + 1;
                    if (percentage == 5 && (flags & 0x2A) != 0)
                    { percentage = random.Next() % 5 + 1; if (percentage > 4) percentage = 6; }
                    code |= (uint)percentage << 16;
                }
                if (!Affordable(code)) continue;
                Store(values, slot, code, hasProtection != 0 ? (ushort)0x8000 : (ushort)0);
                accepted = true;
            }
            return accepted;
        }
        uint selected = 0;
        switch (kind)
        {
            case 2: selected = (uint)(809 + random.Next() % 2); break;
            case 3: selected = (uint)(811 + random.Next() % 2); break;
            case 4:
                if ((((flags & 0x2A) == 0 && (flags & 0x400000) == 0) || allClasses) && weaponType != 6)
                    selected = (flags & 4) == 0 || allClasses ? (uint)(813 + random.Next() % 2) : 813;
                else selected = 814;
                break;
            case 5:
                selected = (uint)(815 + random.Next() % 6);
                if (selected == 819 && ((flags & 0x2A) != 0 || (flags & 0x400000) != 0) && !allClasses || weaponType == 6)
                    selected = random.Next() % 5 < 4 ? (uint)(815 + random.Next() % 4) : 820;
                break;
            case 6:
                var side = random.Next() % 2;
                uint pool = 0;
                if (allClasses)
                {
                    if (usage is 7 or 12 or 13 or 14 && values[26] == 7)
                        pool = side == 0 ? (random.Next() % 2 == 0 ? 18U : 20U) : (random.Next() % 2 == 0 ? 17U : 19U);
                    else pool = side == 0 ? 15U : 14U;
                }
                else if ((flags & 2) != 0) pool = 16;
                else if ((flags & 0x10) != 0) pool = side == 0 ? 20U : 19U;
                else if ((flags & 8) != 0) pool = side == 0 ? 21U : 22U;
                else if ((flags & 4) != 0) pool = 25;
                else if ((flags & 0x20) != 0) pool = side == 0 ? 24U : 23U;
                else if ((flags & 1) != 0) pool = side == 0 ? 18U : 17U;
                if (pool == 0) return false;
                candidates.AddRange(SacredModifierBonusPools.Get(pool).Select(code => (uint)code));
                break;
            case 7:
                var classPools = new (uint Mask, uint Pool)[] { (2, 6), (0x10, 9), (8, 8), (4, 7), (0x20, 11), (1, 10), (0x400000, 12), (0x800000, 13) };
                foreach (var pair in classPools) if ((flags & pair.Mask) != 0) candidates.Add(pair.Pool);
                if (candidates.Count == 0) return false;
                var skills = SacredModifierBonusPools.Get(candidates[Pick(candidates.Count, random)]);
                candidates.Clear();
                foreach (var code in skills)
                    if (category == SacredItemCategory.Weapon ? ranges.Any(row => row.FirstBonusCode == code || row.FirstBonusCode < code && code <= row.LastBonusCode)
                        : category != SacredItemCategory.Shield || code != 605) candidates.Add(code);
                break;
            case 8:
                foreach (var range in ranges)
                {
                    if (range.FirstBonusCode <= 839 || range.LastBonusCode >= 870 || range.FirstBonusCode > range.LastBonusCode) continue;
                    for (var code = range.FirstBonusCode; code <= range.LastBonusCode; code++)
                    {
                        if (code is 858 or 860 or 861 or 862 or 863 || code == 844 && (flags & 0x2A) != 0 ||
                            code == 845 && (flags & 4) != 0 || code == 850 && (flags & 0x20) == 0 || code == 851 && (flags & 0x20) != 0) continue;
                        candidates.Add((uint)code);
                    }
                }
                while (candidates.Count != 0)
                {
                    selected = EraseThenRead(candidates, random);
                    if (Affordable(selected)) { Store(values, slot, selected, 0); return true; }
                }
                return false;
            default: return false;
        }
        if (kind is 6 or 7)
        {
            if (candidates.Count == 0) return false;
            selected = candidates[Pick(candidates.Count, random)];
        }
        if (!Affordable(selected)) return false;
        var type = kind == 6 && allClasses ? unchecked((ushort)(combatArtClass(selected) << 8)) : (ushort)0;
        Store(values, slot, selected, type);
        return true;
    }

    private static int Pick(int count, SacredRandomSequence random) => count == 1 ? 0 : random.Next() % count;
    private static uint EraseThenRead(List<uint> values, SacredRandomSequence random)
    {
        var index = Pick(values.Count, random);
        // Native erases first, then reads the old iterator; the last element's
        // storage remains readable. Preserve this observable selection behavior.
        var result = values[Math.Min(index + 1, values.Count - 1)];
        values.RemoveAt(index); return result;
    }
    private static void Store(Span<byte> values, int slot, uint code, ushort type)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(values[(74 + slot * 4)..], code);
        BinaryPrimitives.WriteUInt16LittleEndian(values[(58 + slot * 2)..], type);
    }

    public static int Family(ushort code) => code switch
    {
        > 800 and < 805 => 0, >= 805 and < 809 => 1, >= 809 and < 811 => 2,
        >= 811 and < 813 => 3, >= 813 and < 815 => 4, >= 815 and < 821 => 5,
        < 201 or (> 999 and < 1201) => 6, > 599 and < 799 => 7, > 839 and < 870 => 8, _ => 0
    };
}
