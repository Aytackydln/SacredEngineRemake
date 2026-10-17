using System;
using Sacred.Inventory.Actors;

namespace Sacred.UI.Inventory;

/// <summary>Equipment selection shared by the inventory panel and its host.</summary>
public sealed class EquipmentUiState
{
    public SacredGameActor? Actor { get; private set; }
    public bool IsOpen { get; private set; }
    public int? SelectedSlot { get; private set; }
    public InventoryPanelLayout? Layout { get; set; }
    public event Action? Changed;

    public void Bind(SacredGameActor actor)
    {
        if (ReferenceEquals(Actor, actor)) return;
        Actor = actor;
        SelectedSlot = null;
        Layout = null;
    }

    public void ClearActor()
    {
        SetOpen(false);
        Actor = null;
        SelectedSlot = null;
        Layout = null;
    }

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        Changed?.Invoke();
    }

    public void Select(int index)
    {
        if (Actor is null || (uint)index >= (uint)Actor.EquipmentSlots.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        SelectedSlot = index;
    }
}
