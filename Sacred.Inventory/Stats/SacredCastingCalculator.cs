using System;
using System.Collections.Generic;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Inventory.Stats;

/// <summary>Gold 0x565DC0 adds a Balance.bin curve to casting speed for the selected,
/// learned art's school. The bonus is flat percentage points, not a speed multiplier.</summary>
public sealed class SacredCastingCalculator(SacredSkillCurveLayout curve)
{
    public float SkillBonus(int rank)
    {
        if (rank < 1) return 0;
        var term = (curve.Limit - (double)curve.Base) * (1 - 1 / ((rank - 1d) / curve.Divisor + 1));
        return (float)(term * 2d + curve.Base);
    }

    public short Calculate(uint creatureType, uint objectFlags, uint valueFlags, uint art,
        IReadOnlyDictionary<byte, int> skills, bool learned)
    {
        var percent = creatureType == 5 ? 100 : 120;
        if (learned && School(art) is { } school)
            percent = unchecked((short)(int)(percent + (double)SkillBonus(skills.GetValueOrDefault(school))));
        if ((objectFlags & 0x2000) != 0) percent = unchecked((short)(percent - 15));
        var cap = (valueFlags & 0x10000) != 0 ? 260 : 220;
        return (short)Math.Clamp(percent, creatureType == 5 ? 80 : 100, cap);
    }

    // Native 0x566070 passes these art banks to 0x565DC0 with cast-speed updates enabled.
    public static byte? School(uint art) => art switch
    {
        13 or 15 or >= 17 and <= 22 or 1019 => 1,
        5 or 11 or 23 or 24 => 15,
        4 or 27 or 34 or 35 => 16,
        2 or 16 or 25 or 26 => 17,
        1 or 28 or 29 or 33 => 18,
        3 or 7 or >= 36 and <= 42 => 19,
        43 or 1022 or >= 1030 and <= 1037 => 28,
        >= 55 and <= 59 or 1085 or 1086 or 1087 => 29,
        _ => null
    };
}
