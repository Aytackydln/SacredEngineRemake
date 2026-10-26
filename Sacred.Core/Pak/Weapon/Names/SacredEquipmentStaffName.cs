using System.Globalization;

namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Gold 0x5C2E50: strict majority of the staff's spell/skill bonus groups.</summary>
public static class SacredEquipmentStaffName
{
    public static string Key(in SacredEquipmentInstanceLayout values, string fallback)
    {
        Span<int> counts = stackalloc int[5];
        counts.Clear();
        for (var i = 0; i < 8; i++)
        {
            var code = (ushort)values.BonusGroups[i];
            var index = code switch
            {
                1 or 28 or 29 or 33 or 617 => 0,
                2 or 16 or 25 or 26 or 616 => 1,
                4 or 27 or 34 or 35 or 615 => 2,
                5 or 11 or 23 or 24 or 614 => 3,
                7 or 30 or 31 or 32 or 618 => 4,
                _ => -1
            };
            if (index >= 0) counts[index]++;
        }
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0) continue;
            var maximum = true;
            for (var j = 0; j < counts.Length; j++) if (j != i && counts[j] >= counts[i]) maximum = false;
            if (!maximum) continue;
            var offset = i switch { 0 => 8U, 1 => 4U, 2 => 0U, 3 => 12U, _ => 16U };
            var key = 2650 + ((values.Flags >> 18) & 3) * 20 + offset + (uint)Math.Clamp(counts[i] - 2, 0, 3);
            return key.ToString(CultureInfo.InvariantCulture);
        }
        return fallback;
    }
}
