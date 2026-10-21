using Sacred.Core.Pak.Creature;

namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x522030 spell animation selection. Slots resolve through Models.tmp;
/// no clip filenames or per-asset substitutions belong here.</summary>
public static class GameplayCastMotion
{
    public static uint? Select(uint code, uint creatureType, SacredCreatureClass creatureClass, bool mounted)
    {
        if (creatureType is 0xB6 or 0xB7) return 0x9A;
        if (creatureClass == SacredCreatureClass.Dragon) return 0x51;
        if (mounted)
            return code switch
            {
                1 or 2 or 4 or 7 or 0x15 or 0x16 or 0x18 or 0x1B or 0x1E or 0x1F or 0x20 or 0x25 or 0x2A => 199,
                >= 3 and <= 0x2B or 0x34 or 0x35 or >= 0x3C and <= 0x43 => 197,
                _ => null
            };
        return code switch
        {
            1 or 2 or 4 or 0x10 or 0x12 or 0x1B or 0x23 => 163,
            3 or 8 or 0xE or 0x2A => 160,
            5 or 0x1D or 0x21 or 0x25 or 0x29 or 0x3A or 0x3B or 0x3D or 0x40 or 0x41 => 155,
            6 => 164,
            7 or 0xC or 0x24 => 161,
            0xB or 0x37 => 156,
            0xD => creatureType is 0x8C or 0x92 ? 147U : 158U,
            0xF => creatureType is 0x8C or 0x92 ? 147U : 154U,
            0x11 => creatureType == 0x8D ? 154U : 157U,
            0x13 or 0x27 => 159,
            0x14 or 0x15 or 0x19 or 0x1E or 0x1F or 0x20 or 0x22 => 157,
            0x16 => creatureType is 0x8C or 0x92 ? 147U : 157U,
            0x17 or 0x2B or 0x34 or 0x39 or 0x3C or 0x3E or 0x3F or 0x43 or >= 0x45 and <= 0x48 => 154,
            0x18 or 0x26 => 162,
            0x1A => creatureType == 0xB7 ? 154U : 157U,
            0x1C or 0x28 or 0x38 or 0x42 => 158,
            0x31 => 144,
            0x32 => 145,
            0x35 => 84,
            // 9/10 require the native archive-presence/ordinary-attack fallback.
            _ => null
        };
    }
}
