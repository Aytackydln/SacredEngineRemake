using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sacred.Engine.Assets;
using Sacred.Inventory.Actors;
using Sacred.Inventory.Items;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Resolves received equipment instance references through current replicated item resources.</summary>
internal static class NetworkActorAppearance
{
    public static string Key(GoldReplicatedObject creature, GoldWorldReplica replicas)
    {
        var key = new StringBuilder().Append(creature.BaseState.ObjectType);
        foreach (var reference in creature.Creature!.EquipmentReferences)
        {
            key.Append(':').Append(reference).Append('/');
            if (replicas.Objects.TryGetValue(reference, out var item) &&
                (item.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 && item.Item is not null)
                key.Append(item.BaseState.ObjectType).Append('/').Append(item.ItemValuesVersion);
            else key.Append(0);
        }
        return key.ToString();
    }

    public static SacredGameActor Create(AssetManager assets, GoldReplicatedObject creature, GoldWorldReplica replicas)
    {
        var item = assets.GetItem(creature.BaseState.ObjectType)!.Value;
        var actor = assets.CreateNetworkActor(assets.CreatureTemplates?.ResolveTemplate(item));
        if (creature.BaseState.ObjectReference == replicas.LocalPlayerSlot && replicas.LocalCharacterValues is { } saved)
            actor.Progression.ImportNativeObjectValues(saved.Block08);
        else if (creature.Values is { IsFull: true } full)
            actor.Progression.ImportNativeObjectValues(full.FullBlock08);
        var instances = new Dictionary<uint, SacredItemInstance>();
        var references = creature.Creature!.EquipmentReferences;
        for (var index = 0; index < references.Length; index++)
        {
            if (!replicas.Objects.TryGetValue(references[index], out var equipped) ||
                (equipped.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
                !assets.TryGetEquipmentDefinition(equipped.BaseState.ObjectType, out var definition) || definition.IsAppearanceFallback) continue;
            var type = index switch
            {
                GoldCreatureStateLayout.LeftHandEquipmentIndex => EquipmentSlotType.LeftHand,
                GoldCreatureStateLayout.RightHandEquipmentIndex => EquipmentSlotType.RightHand,
                _ => EquipmentSlotRules.GetSlotType(definition)
            };
            var slots = actor.EquipmentSlots.Where(slot => EquipmentSlotRules.Accepts(slot.Type, type)).ToArray();
            var slot = type == EquipmentSlotType.Ring && index is >= 8 and <= 11
                ? slots.ElementAtOrDefault(index - 8)
                : type == EquipmentSlotType.Amulet && index is 6 or 7
                    ? slots.ElementAtOrDefault(index - 6) : slots.FirstOrDefault(slot => slot.Equipment is null);
            // Received authority wins; do not run offline two-handed equip rules over native slots.
            if (equipped.Item is { } itemState)
            {
                if (!instances.TryGetValue(references[index], out var instance))
                    instances[references[index]] = instance = SacredItemInstance.FromNativeObjectRecords(definition, equipped.BaseState.ObjectType, itemState.ItemData, itemState.Records);
                slot?.Equip(instance);
            }
        }
        actor.SetNativeMovementEquipment(instances.Values);
        return actor;
    }
}
