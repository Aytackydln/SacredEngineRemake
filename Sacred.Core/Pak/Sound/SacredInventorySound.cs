using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Sound;

/// <summary>Sound.pak identifiers named by Sacred.exe's embedded sound registry.</summary>
public enum SacredInventorySound : uint
{
    PutMetal = 7005,
    PutRing = 7007,
    Potion = 7019,
    PutAmulet = 7020,
    PutQuestBook = 7107,
    PutHelmet = 7113,
    PutKeys = 7114,
    PutShield = 7115,
    PutClothes = 7116,
}

/// <summary>
/// Reproduces the inventory-sound category switch in Sacred.exe. Its discriminator is
/// <see cref="SacredItemCategory"/> at Items.pak model-descriptor byte 0x2E.
/// </summary>
public static class SacredInventorySoundResolver
{
    public static SacredInventorySound Resolve(SacredItemCategory category) =>
        category switch
        {
            SacredItemCategory.ChestArmor or
                SacredItemCategory.FootArmor or
                SacredItemCategory.Belt or
                SacredItemCategory.ShoulderArmor or
                SacredItemCategory.LegArmor or
                SacredItemCategory.Gloves => SacredInventorySound.PutClothes,
            SacredItemCategory.Ring => SacredInventorySound.PutRing,
            SacredItemCategory.Potion => SacredInventorySound.Potion,
            SacredItemCategory.Shield => SacredInventorySound.PutShield,
            SacredItemCategory.Key => SacredInventorySound.PutKeys,
            SacredItemCategory.Book => SacredInventorySound.PutQuestBook,
            SacredItemCategory.Helmet => SacredInventorySound.PutHelmet,
            SacredItemCategory.Amulet or SacredItemCategory.Rune => SacredInventorySound.PutAmulet,
            _ => SacredInventorySound.PutMetal,
        };
}
