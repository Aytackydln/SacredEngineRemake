using System;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Engine.Assets;
using Sacred.Inventory.Stats;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

/// <summary>Calculated action speed for ordinary unbuffed NPCs. Full received values
/// take precedence. Hero skills, over-level equipment and active effects need their complete calculators.</summary>
internal static class NetworkNpcAnimationSpeed
{
    public static SacredNpcAnimationTiming? Resolve(AssetManager assets, GoldWorldReplica world, GoldReplicatedObject source, float motionFactor)
    {
        short? speed = source.Values?.AttackSpeed;
        if (speed is null && source.Values is { Word148: 0 } values &&
            assets.GetItem(source.BaseState.ObjectType) is { } item &&
            assets.CreatureTemplates?.ResolveTemplate(item) is { Class: not SacredCreatureClass.Hero } &&
            source.Creature!.EquipmentReferences.ToArray().Where(reference => reference != 0).All(reference =>
                world.Objects.TryGetValue(reference, out var equipped) && equipped.Item is not null &&
                (equipped.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0 &&
                assets.TryGetEquipmentDefinition(equipped.BaseState.ObjectType, out var definition) && !definition.IsAppearanceFallback))
        {
            var actor = NetworkActorAppearance.Create(assets, source, world);
            actor.Progression.SetProgression(values.Level, values.Experience, values.Gold, values.RemainingSkillPoints);
            var equipment = actor.Inventory.EquippedItems.ToArray();
            // The supported branch of Gold CalcResults starts non-hero attack speed at 140.
            // Its Creature.pak BonusAuf pass does not write attack speed; attribute-scaled
            // equipment bonuses still need the complete attribute calculation below.
            // Do not claim the weapon-level penalty or attribute-scaled speed bonuses without their inputs.
            if (equipment.All(entry => entry.Level <= values.Level && entry.Bonuses.All(bonus => bonus.Code != 811 || bonus.AttributeSelector == 0)))
                speed = (short)Math.Clamp((int)unchecked((short)(140 + (int)actor.EquipmentStats[SacredActorStat.AttackSpeed])), 100, 220);
        }
        var factor = source.Creature!.AnimationSpeedMultiplier;
        if (speed is not > 0 || !float.IsFinite(factor) || factor <= 0) return null;
        return SacredNpcAnimationTiming.Conversation(speed.Value, factor, motionFactor);
    }
}
