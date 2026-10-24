using System;
using System.Linq;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Assets;
using Sacred.GameplayHost.World;
using Sacred.Inventory.Actors;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Offline adapter to the same traced Gold motion selectors used for received actions.</summary>
internal static class PlayerCombatArtRules
{
    public static uint CreatureType(AssetManager assets, PlayerCombatContext context)
    {
        var template = assets.GetItem(context.Asset.ItemId) is { } item ? assets.CreatureTemplates?.ResolveTemplate(item) : null;
        return template?.ItemType ?? context.Actor.Progression.Template?.ItemType ?? 0;
    }

    public static uint ObjectFlags(uint type) => type == 7 ? 0x2000U : 0;

    public static GameplayCombatArtAnimation? Select(AssetManager assets, PlayerCombatContext context,
        ushort code, SacredCombatArtKind kind, bool selfTarget, out short castSpeed)
    {
        var actor = context.Actor;
        var type = CreatureType(assets, context);
        var flags = ObjectFlags(type);
        castSpeed = assets.CastingCalculator?.Calculate(type, flags, 0, code, actor.TotalStats.Skills, true)
            ?? GameplayCastSpeed.UnmodifiedPercent(type, flags);
        if (kind == SacredCombatArtKind.Spell)
        {
            var slot = GameplayCastMotion.Select(code, type, actor.Progression.Template?.Class ?? 0, false);
            return slot is { } motion && castSpeed > 0 ? new(motion, castSpeed * .01f, .2f) : null;
        }
        var left = actor.EquipmentSlots.FirstOrDefault(slot => slot.Type == EquipmentSlotType.LeftHand)?.Equipment;
        var right = actor.EquipmentSlots.FirstOrDefault(slot => slot.Type == EquipmentSlotType.RightHand)?.Equipment;
        ushort stance = left is { EquipmentType: not SacredEquipmentType.Shield } && right is not null
            ? (ushort)(left.Value.UsageIdentifier == 0 && right.Value.UsageIdentifier == 0 ? 0 : 8)
            : (ushort)(right?.UsageIdentifier ?? left?.UsageIdentifier ?? 0);
        if (stance == 14) stance = 12;
        GameplayCombatArtAnimation? selected;
        if (type is 6 or 7 && code is >= 1030 and <= 1037)
            selected = GameplayVampiressArtMotion.Select(code, flags, false);
        else if (GameplayDaemonTargetedArt.Handles(type, code))
            selected = GameplayDaemonTargetedArt.Select(type, code, false, selfTarget &&
                assets.GetItem(context.Asset.ItemId) is { } target &&
                GameplayDaemonTargetedArt.IsTargetCategory((byte)target.ModelDesc.Category));
        else if (type is 6 or 7 && (flags & 0x2000) != 0 && code is 1073 or 1074 or 1075 && stance is not (7 or 12))
            selected = GameplayVampiressArtMotion.Select(code, flags, false);
        else selected = GameplayCombatArtMotion.SelectSpecialMove(code, type, stance);
        if (selected is not { } art || art.RateSource == GameplayCombatArtRateSource.Fixed) return selected;
        if (art.RateSource == GameplayCombatArtRateSource.CastSpeed)
            return castSpeed > 0 ? art with { Speed = art.Speed * castSpeed * .01f } : null;
        if (assets.AttackSpeedCalculator is not { } calculator) return null;
        var skills = actor.Progression.Template?.StartingSkills.Concat(actor.TotalStats.Skills.Keys).Distinct().ToArray() ?? [];
        var level = actor.Inventory.EquippedItems.Where(item => item.Definition.Item.ModelDesc.Category is
            SacredItemCategory.Weapon or SacredItemCategory.Projectile).Select(item => (int)item.Level).DefaultIfEmpty().Max();
        var speed = calculator.Calculate(type, flags, 0, actor.Progression.Level,
            skills, actor.TotalStats, stance, left, right, level);
        return speed > 0 ? art with { Speed = art.Speed * Math.Min(220, (int)speed) * .01f } : null;
    }
}
