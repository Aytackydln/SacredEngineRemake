using System;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Supplies received values and archive equipment inputs to shared action rules.</summary>
internal static class NetworkCombatArtSelection
{
    public static GameplayCombatArtAnimation? Resolve(AssetManager assets, GoldWorldReplica world, GoldReplicatedObject actor)
    {
        if (actor.Cast is { } cast)
        {
            var template = assets.GetItem(actor.BaseState.ObjectType) is { } item
                ? assets.CreatureTemplates?.ResolveTemplate(item) : null;
            var mounted = (actor.BaseState.Flags & 2) != 0 && actor.MountPartnerReference is > 0;
            var motion = GameplayCastMotion.Select(cast.Code, actor.BaseState.ObjectType,
                template?.Class ?? SacredCreatureClass.Unknown, mounted);
            var speed = NetworkCastSpeed.ResolveForArt(assets, world, actor, cast.Code);
            return motion is { } slot && speed is > 0 ? new(slot, speed.Value * .01f, .2f) : null;
        }
        if (actor.SpecialMove is not { } special) return null;
        SacredEquipment? Equipment(int index) => world.Objects.TryGetValue(actor.Creature!.EquipmentReferences[index], out var equipped) &&
            (equipped.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 && equipped.Item is not null &&
            assets.TryGetEquipmentDefinition(equipped.BaseState.ObjectType, out var definition) && !definition.IsAppearanceFallback
                ? definition : null;
        var left = Equipment(GoldCreatureStateLayout.LeftHandEquipmentIndex);
        var right = Equipment(GoldCreatureStateLayout.RightHandEquipmentIndex);
        // Gold 0x546F80: an offhand shield preserves main-hand usage; two weapons select dual wield.
        ushort stance = left is { EquipmentType: not SacredEquipmentType.Shield } && right is not null
            ? (ushort)(left.Value.UsageIdentifier == 0 && right.Value.UsageIdentifier == 0 ? 0 : 8)
            : (ushort)(right?.UsageIdentifier ?? left?.UsageIdentifier ?? 0);
        if (stance == 14) stance = 12;
        var specialMounted = (actor.BaseState.Flags & 2) != 0 && actor.MountPartnerReference is > 0;
        if (actor.BaseState.ObjectType is 6 or 7 && special.Code is >= 1030 and <= 1037)
            return GameplayVampiressArtMotion.Select(special.Code, actor.BaseState.Flags, specialMounted);
        if (GameplayDaemonTargetedArt.Handles(actor.BaseState.ObjectType, special.Code))
            return GameplayDaemonTargetedArt.Select(actor.BaseState.ObjectType, special.Code, specialMounted,
                world.Objects.TryGetValue(special.TargetReference, out var target) && assets.GetItem(target.BaseState.ObjectType) is { } targetItem &&
                GameplayDaemonTargetedArt.IsTargetCategory((byte)targetItem.ModelDesc.Category));
        if (specialMounted) return null;
        var selected = actor.BaseState.ObjectType is 6 or 7 && (actor.BaseState.Flags & 0x2000) != 0 &&
            special.Code is 1073 or 1074 or 1075 && stance is not (7 or 12)
                ? GameplayVampiressArtMotion.Select(special.Code, actor.BaseState.Flags, false)
                : GameplayCombatArtMotion.SelectSpecialMove(special.Code, actor.BaseState.ObjectType, stance);
        if (selected is not { } art || art.RateSource == GameplayCombatArtRateSource.Fixed) return selected;
        var percent = art.RateSource == GameplayCombatArtRateSource.CastSpeed
            ? NetworkCastSpeed.ResolveForArt(assets, world, actor, special.Code)
            : NetworkAttackSpeed.Resolve(assets, world, actor, stance, left, right);
        return percent is > 0 ? art with { Speed = art.Speed *
            (art.RateSource == GameplayCombatArtRateSource.AttackSpeed ? Math.Min(220, (int)percent.Value) : percent.Value) * .01f } : null;
    }
}
