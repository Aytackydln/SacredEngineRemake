using System.Collections.Generic;
using Sacred.Core.Pak.Weapon;

namespace Sacred.UI.Hud;

/// <summary>Presentation of one native hand pair; inactive sets do not affect actor statistics.</summary>
public readonly record struct HudWeaponSet(SacredEquipment? MainHand, SacredEquipment? Offhand)
{
    public static IEnumerable<SacredEquipment> Equipment(IEnumerable<HudWeaponSet> sets)
    {
        foreach (var set in sets)
        {
            if (set.MainHand is { } main) yield return main;
            if (set.Offhand is { } offhand) yield return offhand;
        }
    }
}
