using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.UI;
using Sacred.Inventory.Actors;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>cUI_Equipment's six slot systems. Coordinates are local to its 256 by 256 panel.</summary>
public sealed class EquipmentSlotsLayout
{
    public const int Width = 256;
    public const int Height = 256;
    public IReadOnlyList<EquipmentSlotPresentation> Slots { get; }

    public EquipmentSlotsLayout(SacredCharacterClass characterClass,
        IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        var system = characterClass switch
        {
            SacredCharacterClass.Seraphim => 4,
            SacredCharacterClass.Gladiator => 1,
            SacredCharacterClass.BattleMage or SacredCharacterClass.WoodElf => 2,
            SacredCharacterClass.DarkElf or SacredCharacterClass.Vampiress => 3,
            SacredCharacterClass.Dwarf => 5,
            SacredCharacterClass.Daemon => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
        };
        Slots = Definitions(system).Select(d => new EquipmentSlotPresentation(d.Type, d.Occurrence,
            d.Index, new UiTextureRegion(table[d.Selector]), new(d.X, d.Y))).ToArray();
    }

    private static IEnumerable<SlotDefinition> Definitions(int system)
    {
        // Demo setSlotsSystem1..4 and Gold/pureHD setSlotsSystem1..6.
        // Selectors address sUITextureDef; they are not texture.pak IDs.
        yield return new(EquipmentSlotType.Head, 0, 146, 96, 6);
        yield return new(EquipmentSlotType.Body, 1, 147, system == 4 ? 16 : 96, 80);
        yield return new(EquipmentSlotType.Feet, 3, 150, system == 4 ? 96 : 16, 186);
        if (system != 4) yield return new(EquipmentSlotType.Hands, 4, 151, 16, 146);
        if (system != 5) yield return new(EquipmentSlotType.Arms, 5, 152, 16, system == 4 ? 186 : 76);
        if (system is 1 or 3 or 4 or 5 or 6)
            yield return new(EquipmentSlotType.Shoulder, 15, 162, system == 5 ? 16 : 176, system == 5 ? 76 : 6);
        if (system is not (4 or 5)) yield return new(EquipmentSlotType.Legs, 16, 163, 96, 186);
        if (system == 4) yield return new(EquipmentSlotType.Wings, 17, 164, 96, 80);
        if (system == 5) yield return new(EquipmentSlotType.Cannon, 14, 1611, 176, 6);

        var largeBelt = system is 1 or 4;
        yield return new(largeBelt ? EquipmentSlotType.Belt : EquipmentSlotType.SmallBelt, 2,
            largeBelt ? 148 : 149, system == 5 ? 96 : 176, largeBelt || system == 5 ? 186 : 218);
        var twoAmulets = system is 2 or 5;
        var amuletY = system == 4 ? 74 : system == 5 ? 138 : 82;
        yield return new(EquipmentSlotType.Amulet, 6, 153, twoAmulets ? 176 : system == 4 ? 208 : 192, amuletY);
        if (twoAmulets) yield return new(EquipmentSlotType.Amulet, 7, 154, 208, amuletY, 1);
        var ringCount = system is 1 or 4 ? 2 : 4;
        for (var ring = 0; ring < ringCount; ring++)
        {
            var x = system == 4 ? 208 : 176 + ring % 2 * 32;
            var y = system == 4 ? 112 + ring * 36 : system == 5 ? 172 + ring / 2 * 34 : 120 + ring / 2 * 38;
            yield return new(EquipmentSlotType.Ring, 8 + ring, system is 1 or 4 ? 155 : 155 + ring, x, y, ring);
        }
    }

    private sealed record SlotDefinition(EquipmentSlotType Type, int Index, int Selector, int X, int Y, int Occurrence = 0);
}
