using System;
using Sacred.Inventory.Actors;
using Sacred.UI.CombatArts;

namespace Sacred.UI.Inventory;

/// <summary>Character binding, page selection and equipment selection shared by inventory and its host.</summary>
public sealed class EquipmentUiState
{
    public SacredGameActor? Actor { get; private set; }
    public bool IsOpen { get; private set; }
    public int? SelectedSlot { get; private set; }
    public InventoryPanelLayout? Layout { get; set; }
    public InventoryTab ActiveTab { get; private set; }
    public CombatArtPanelState CombatArts { get; } = new();
    public event Action? Changed;

    public void Bind(SacredGameActor actor)
    {
        if (ReferenceEquals(Actor, actor)) return;
        Actor = actor;
        SelectedSlot = null;
        Layout = null;
        CombatArts.Bind(actor);
    }

    public void ClearActor()
    {
        SetOpen(false);
        Actor = null;
        SelectedSlot = null;
        Layout = null;
        ActiveTab = InventoryTab.Inventory;
        CombatArts.Bind(null);
    }

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        CombatArts.CancelSelection();
        Changed?.Invoke();
    }

    public void SetTab(InventoryTab tab)
    {
        if (!Enum.IsDefined(tab)) throw new ArgumentOutOfRangeException(nameof(tab));
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        CombatArts.CancelSelection();
        Changed?.Invoke();
    }

    public void Select(int index)
    {
        if (Actor is null || (uint)index >= (uint)Actor.EquipmentSlots.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        SelectedSlot = index;
    }
}
