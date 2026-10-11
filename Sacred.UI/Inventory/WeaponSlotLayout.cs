using System.Numerics;
using Sacred.Core.Pak.Weapon;

namespace Sacred.UI.Inventory;

/// <summary>The native taskbar hand preview's item anchor, using signed Weapon.pak offsets.</summary>
public static class WeaponSlotLayout
{
    public static Vector2 ItemCenter(SacredEquipment item) =>
        new(item.Width * 16 - item.TaskbarOffsetX + 2, item.Height * 16 - item.TaskbarOffsetY + 5);
}
