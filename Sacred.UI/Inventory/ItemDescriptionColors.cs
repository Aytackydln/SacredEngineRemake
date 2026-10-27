using Sacred.Core.Pak.Items;

namespace Sacred.UI.Inventory;

/// <summary>Native Gold item-name and modifier ARGB colors (pureHD 0x5C1830 / 0x5C48C0).</summary>
public static class ItemDescriptionColors
{
    public const uint Text = 0xFFFFFFFF;
    public const uint Set = 0xFF15FF25;
    public const uint Damage = 0xFF00FF00;

    public static uint Name(uint flags, byte setType, SacredItemCategory category)
    {
        if (setType != 0) return Set;
        var classes = flags & 0xC0003F;
        // Default native class-color option; Wings always uses the class color path.
        if (classes is not (0 or 0xC0003F) || category == SacredItemCategory.Wings)
        {
            var color = 0xFFEFEFEFu;
            foreach (var (mask, value) in ClassColors)
                if ((flags & mask) != 0) color = value;
            return color;
        }
        var rarity = (flags >> 16) & 15;
        return rarity <= 4 ? 0xFFEFEFEF : rarity < 12 ? 0xFFA0A0FF : rarity < 15 ? 0xFFF0E040 : 0xFFC19A33;
    }

    public static uint Modifier(ushort code) => code switch
    {
        801 or 805 => 0xFFFFFFA0,
        802 or 806 => 0xFFFFA0A0,
        803 or 807 => 0xFFA0A0FF,
        804 or 808 => 0xFFA0FFA0,
        >= 840 and <= 868 when code != 854 => 0xFFD8AB30,
        _ => 0xFF449BEA
    };

    private static readonly (uint Mask, uint Color)[] ClassColors =
    [
        (1, 0xFF87CEFA), (2, 0xFFE38217), (4, 0xFF6C4CE5), (8, 0xFFA020F0),
        (16, 0xFF228B22), (32, 0xFFB75555), (0x400000, 0xFF00FFFF), (0x800000, 0xFFFF6C00)
    ];
}
