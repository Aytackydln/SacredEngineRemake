using System.Collections.Generic;
using System.Linq;
using Sacred.Core.UI;
using Sacred.Inventory.Actors;

namespace Sacred.UI.Inventory;

/// <summary>Actor-bound armor/accessory slots in equipment-local coordinates, independent of stats.</summary>
public sealed class EquipmentPanelLayout
{
    public IReadOnlyList<InventoryPanelSlot> Slots { get; }

    public EquipmentPanelLayout(SacredGameActor actor, IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions)
    {
        var slots = new List<InventoryPanelSlot>();
        foreach (var slot in new EquipmentSlotsLayout(actor.CharacterClass, definitions).Slots)
        {
            var domain = actor.EquipmentSlots.Select((value, index) => (value, index))
                .Where(s => s.value.Type == slot.Type).Skip(slot.Occurrence).First();
            slots.Add(new(domain.index, slot.Art, slot.Position));
        }
        Slots = slots.AsReadOnly();
    }
}
