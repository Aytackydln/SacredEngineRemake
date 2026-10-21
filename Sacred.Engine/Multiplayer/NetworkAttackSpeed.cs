using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class NetworkAttackSpeed
{
    public static short? Resolve(AssetManager assets, GoldWorldReplica world, GoldReplicatedObject actor,
        ushort stance, SacredEquipment? left, SacredEquipment? right)
    {
        if (actor.Values?.AttackSpeed is { } received) return received;
        if (actor.BaseState.ObjectReference == world.LocalPlayerSlot && world.LocalCharacterValues is { } saved)
            return BinaryPrimitives.ReadInt16LittleEndian(saved.Block08[52..]);
        if (actor.Values is not { Level: > 0 } values || actor.Creature is null ||
            assets.AttackSpeedCalculator is not { } calculator) return null;
        var highestWeaponLevel = 0;
        foreach (var reference in actor.Creature.EquipmentReferences)
        {
            if (reference == 0) continue;
            if (!world.Objects.TryGetValue(reference, out var equipped) ||
                (equipped.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 || equipped.Item is not { } item) return null;
            Consider(item.Instance);
            for (var index = 0; index < item.Count && index < 8; index++)
                if (item.Instance.SlotTypes[index] is >= 1 and <= 4)
                    Consider(MemoryMarshal.Read<SacredEquipmentInstanceLayout>(item.Records[(index * GoldItemStateLayout.ItemDataSize)..]));
        }
        var loadout = NetworkActorAppearance.Create(assets, actor, world);
        loadout.Progression.ImportNativeProgression(values.Level, values.Experience, values.Gold, values.RemainingSkillPoints,
            values.Block1C, values.Block24, values.Block2C, values.Byte58);
        return calculator.Calculate(actor.BaseState.ObjectType, actor.BaseState.Flags, values.Word148, values.Level,
            values.Block24, loadout.TotalStats, stance, left, right, highestWeaponLevel);

        void Consider(SacredEquipmentInstanceLayout record)
        {
            // CalcResults 0x579F54 counts native categories 5 and 27, including populated sockets.
            if (assets.GetItem(record.ItemType) is { } item && (byte)item.ModelDesc.Category is 5 or 27)
                highestWeaponLevel = Math.Max(highestWeaponLevel, record.Level);
        }
    }
}
