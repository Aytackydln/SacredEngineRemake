using System.Linq;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.Inventory.Actors;
using Sacred.Network.Gameplay;
using Sacred.UI.Hud;

namespace Sacred.Engine.Multiplayer;

/// <summary>Resolves saved hand pairs through received objects, including late item arrivals.</summary>
internal sealed class NetworkQuickbarUiBinding(AssetManager assets, BottomHudState hud)
{
    private long _revision = -1;
    private SacredGameActor? _actor;

    public void Update(GoldWorldReplica world, SacredGameActor actor)
    {
        if (_revision == world.Revision && ReferenceEquals(_actor, actor)) return;
        var inventory = world.FindInventory(world.LocalPlayerSlot);
        hud.SelectedWeaponSlot = inventory?.SelectedWeaponSlot ?? 0;
        hud.SelectedCombatArtSlot = inventory?.SelectedCombatArtSlot ?? 0;
        var sets = new HudWeaponSet[SacredQuickSlotDefinitions.MaximumSlots];
        for (var index = 0; index < sets.Length; index++)
        {
            // The original taskbar synchronizes its selected pair from the creature's actual equipment.
            sets[index] = index == hud.SelectedWeaponSlot
                ? new(actor.EquipmentSlots.FirstOrDefault(s => s.Type == EquipmentSlotType.RightHand)?.Equipment,
                    actor.EquipmentSlots.FirstOrDefault(s => s.Type == EquipmentSlotType.LeftHand)?.Equipment)
                : new(Resolve(inventory?.GetWeaponSetReference(index, true) ?? 0),
                    Resolve(inventory?.GetWeaponSetReference(index, false) ?? 0));
        }
        hud.WeaponSets = sets;
        _actor = actor;
        _revision = world.Revision;

        SacredEquipment? Resolve(uint reference) => world.Objects.TryGetValue(reference, out var item) &&
            item.Item is not null && (item.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 &&
            assets.TryGetEquipmentDefinition(item.BaseState.ObjectType, out var equipment) && !equipment.IsAppearanceFallback
                ? equipment : null;
    }
}
