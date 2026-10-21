using System.Buffers.Binary;
using Sacred.Engine.Assets;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Full calculated values take precedence; compact values supply progression
/// and equipment for the selected art's casting school.</summary>
internal static class NetworkCastSpeed
{
    public static short? ResolveForArt(AssetManager assets, GoldWorldReplica world, GoldReplicatedObject actor, uint code)
    {
        var fallback = Resolve(world, actor);
        if (fallback is null || actor.BaseState.ObjectReference == world.LocalPlayerSlot && world.LocalCharacterValues is not null ||
            actor.Values is not { IsFull: false, Level: > 0 } values || assets.CastingCalculator is not { } calculator)
            return fallback;
        var loadout = NetworkActorAppearance.Create(assets, actor, world);
        loadout.Progression.ImportNativeProgression(values.Level, values.Experience, values.Gold, values.RemainingSkillPoints,
            values.Block1C, values.Block24, values.Block2C, values.Byte58);
        var learned = false;
        for (var index = 0; index < values.Count22; index++)
            learned |= BinaryPrimitives.ReadUInt16LittleEndian(values.GetRecord22(index)[4..]) == code;
        return calculator.Calculate(actor.BaseState.ObjectType, actor.BaseState.Flags, values.Word148,
            code, loadout.TotalStats.Skills, learned);
    }

    public static short? Resolve(GoldWorldReplica world, GoldReplicatedObject actor)
    {
        if (actor.Values?.CastSpeed is { } received) return received;
        if (actor.BaseState.ObjectReference == world.LocalPlayerSlot && world.LocalCharacterValues is { } saved)
            return BinaryPrimitives.ReadInt16LittleEndian(saved.Block08[54..]);
        if (actor.Values is null || actor.Creature is null) return null;
        foreach (var reference in actor.Creature.EquipmentReferences)
        {
            if (reference == 0) continue;
            if (!world.Objects.TryGetValue(reference, out var equipped) ||
                (equipped.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
                equipped.Item is null) return null;
        }
        // Gold 0x57C6B5 gates weapon-level attack-speed penalties separately.
        // An over-level ordinary weapon does not invalidate the casting result.
        return GameplayCastSpeed.UnmodifiedPercent(actor.BaseState.ObjectType, actor.BaseState.Flags);
    }
}
