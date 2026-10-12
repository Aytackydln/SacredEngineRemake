using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.UI;
using Sacred.Inventory.Actors;
using Sacred.UI.Hud;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>One taskbar frame containing both hands, in native draw order.</summary>
public sealed class WeaponQuickSlotLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
{
    public UiTextureRegion Frame { get; } = new(table[103]);
    public UiTextureRegion Empty { get; } = BottomHudSlotBackgrounds.Hand(table);

    public static IEnumerable<WeaponQuickSlotItem> Items(SacredEquipment? mainHand, SacredEquipment? offhand)
    {
        // Demo cUI_Taskbar2::render: two weapons receive opposing eight-pixel corrections.
        var dualWeapons = mainHand?.Item.ModelDesc.Category == SacredItemCategory.Weapon &&
            offhand?.Item.ModelDesc.Category == SacredItemCategory.Weapon;
        if (mainHand is { } main) yield return new(EquipmentSlotType.RightHand, main, new(dualWeapons ? 8 : 0, 0));
        if (offhand is { } off) yield return new(EquipmentSlotType.LeftHand, off, new(dualWeapons ? -8 : 0, 0));
    }
}

public sealed record WeaponQuickSlotItem(EquipmentSlotType Hand, SacredEquipment Equipment, Vector2 Offset);
