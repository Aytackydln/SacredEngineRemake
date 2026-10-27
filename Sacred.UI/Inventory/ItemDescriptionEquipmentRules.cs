using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.UI.Inventory;

/// <summary>Native weapon-description rules. Resource-specific two-handed cases are read from the executable.</summary>
public sealed class ItemDescriptionEquipmentRules(IReadOnlyList<uint> twoHandedBases)
{
    public bool IsTwoHanded(SacredEquipment item) => item.Item.ModelDesc.Category == SacredItemCategory.Weapon &&
        (item.UsageIdentifier is 2 or 3 or 4 or 7 or 12 || twoHandedBases.Contains(item.BaseItemId == 0 ? item.IdemId : item.BaseItemId));

    /// <summary>Nonempty weapon path of native GetWeaponSkill, pureHD 0x579180.
    /// Uses the original shared subtype and usage, independently of generated subtype mutation.</summary>
    public static byte WeaponSkill(SacredEquipment item)
    {
        if (item.Item.ModelDesc.Category != SacredItemCategory.Weapon) return 0;
        var type = item.Classification.EquipmentTypeCode;
        byte skill = type is 30 or 31 ? (byte)7 : item.UsageIdentifier == 8 ? (byte)6
            : type == 10 ? (byte)13 : item.UsageIdentifier == 0 ? (byte)24 : (byte)0;
        if (type <= 4) return 4;
        if (type is 5 or 6 or 22) return 5;
        if (type is 21 or 23 or 24) return 3;
        return item.UsageIdentifier is 7 or 12 or 13 or 14 ? (byte)7 : skill;
    }

    /// <summary>Native 0x5C9090 maps generation difficulty to UI selectors 414..418.</summary>
    public static int DifficultyEmblem(uint flags) => ((flags >> 24) & 15) switch
    {
        2 => 415, 3 => 416, 4 => 417, 5 => 418, _ => 414
    };
}
