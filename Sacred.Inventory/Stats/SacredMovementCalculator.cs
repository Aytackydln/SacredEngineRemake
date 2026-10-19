using System;
using System.Collections.Generic;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Bonuses;
using Sacred.Inventory.Items;

namespace Sacred.Inventory.Stats;

/// <summary>Gold on-foot hero movement: CalcResults 0x5796A0 and path speed 0x4F75F0.
/// Mounted/follower inheritance and active combat-art effects are separate native stages.</summary>
public sealed class SacredMovementCalculator(SacredMovementBalance balance,
    IReadOnlyList<SacredWeaponModifier> modifiers, IReadOnlyDictionary<uint, SacredEquipment> equipment)
{
    /// <summary>Gold 0x4F75F0/0x413610: the path factor before integer speed truncation.
    /// The selected motion's Models.tmp multiplier is applied by animation playback.</summary>
    public float AnimationPlaybackSpeed(short percent, float actorFactor = 1)
    {
        if (!float.IsFinite(actorFactor) || actorFactor < 0) throw new ArgumentOutOfRangeException(nameof(actorFactor));
        var factor = (float)(percent * (double)actorFactor * .01f);
        return factor * balance.MovementScale;
    }

    public int BasePercent(byte horseClass) => balance.GetProfile(horseClass).MovementSpeedPercent;
    public int BasePercent(SacredCharacterProgression progression) => progression.Template?.Class == SacredCreatureClass.Horse
        ? BasePercent(progression.MovementProfileSelector) : progression.Template?.ItemType == 5 ? 120 : 130;

    public SacredMovementSpeeds Calculate(SacredCharacterProgression progression, SacredActorStats inherent,
        IReadOnlyList<SacredItemInstance> items, float actorFactor = 1,
        uint objectFlags = 0, uint valueFlags = 0, uint behaviorFlags = 0)
    {
        var template = progression.Template ?? throw new InvalidOperationException("Movement needs a creature template.");
        if (template.Class != SacredCreatureClass.Hero)
            throw new InvalidOperationException("This calculation requires a hero template.");
        var percent = unchecked((short)BasePercent(progression));
        var penalty = 1f;
        var armorLoreBase = inherent.Skills.GetValueOrDefault((byte)11);
        var armorLoreBonus = 0;
        var attributes = inherent;
        foreach (var item in items)
        {
            if (item.NativeValues is { } main)
            {
                ApplyRecord(main);
                for (var i = 0; i < item.SocketRecords.Count && i < 8; i++)
                    if (main.SlotTypes[i] is >= 1 and <= 4) ApplyRecord(item.SocketRecords[i]);
            }
            else
            {
                ApplyPenalty(item.Definition.IdemId, item.Level);
                foreach (var bonus in item.Bonuses) ApplyBonus(bonus);
            }

            void ApplyRecord(SacredEquipmentInstanceLayout source)
            {
                ApplyPenalty(source.ItemType, source.Level);
                for (var i = 0; i < 8; i++)
                    ApplyBonus(SacredEquipmentBonusDecoder.Decode(source.BonusGroups[i], source.BonusTypes[i], source.BonusValues[i]));
            }

            void ApplyPenalty(uint itemType, int level)
            {
                if (!equipment.TryGetValue(itemType, out var original)) return;
                var category = original.Item.ModelDesc.Category;
                var lore = Math.Clamp(armorLoreBase + armorLoreBonus, 0, byte.MaxValue);
                if (category is not (SacredItemCategory.ChestArmor or SacredItemCategory.Shield or SacredItemCategory.Helmet or
                    SacredItemCategory.FootArmor or SacredItemCategory.Belt or SacredItemCategory.ShoulderArmor or
                    SacredItemCategory.ArmArmor or SacredItemCategory.LegArmor or SacredItemCategory.Gloves or SacredItemCategory.Wings) ||
                    level <= progression.Level + lore) return;
                var resource = item.Definition.IdemId;
                // Gold 0x42CA70 explicitly follows BaseItemId only for these native armor families.
                if (resource is > 2000 and < 2999 or > 6500 and < 7999)
                {
                    var inherited = item.Definition.BaseItemId;
                    if (inherited is >= 2000 and < 3000 or >= 6500 and < 8000) return;
                    resource = inherited;
                }
                SacredWeaponModifier? modifier = null;
                foreach (var entry in modifiers)
                {
                    if (!Contains(entry.ItemIds, resource)) continue;
                    modifier = entry;
                    break;
                }
                if (modifier?.Definition.Modifier is not (> 0 and < 1000)) return;
                var ratio = (progression.Level + lore + 9.0) / (level + 9.0);
                penalty = Math.Min(penalty, (float)(1 - (1 - Math.Pow(ratio, 2)) * modifier.Definition.Modifier * .01));
            }
        }
        // The skill curve uses the sum of the two bytes; getSkillLevel used by penalties clamps it.
        var armorLore = Math.Max(0, armorLoreBase + armorLoreBonus);
        // Gold 0x568058..0x5681EE: Armor Lore also adds movement speed after equipment bonuses.
        if (armorLore > 0)
        {
            var curve = balance.ArmorLoreMovement;
            var bonus = (float)(2 * (curve.Limit - (double)curve.Base) *
                (1 - 1 / ((armorLore - 1.0) / curve.Divisor + 1)) + curve.Base);
            percent = unchecked((short)(int)(percent + (double)bonus));
        }
        if (penalty < 1) percent = unchecked((short)(int)(percent * (double)penalty));
        if ((objectFlags & 0x2000) != 0) percent = unchecked((short)(percent - 15));
        // Gold's battle mage has the native 80% floor; other heroes use 100%.
        // Ordinary creatures compare against 220 even when the replacement cap is 260.
        if (percent > 220) percent = (short)((valueFlags & 0x10000) != 0 ? 260 : 220);
        percent = Math.Max(percent, (short)(template.ItemType == 5 ? 80 : 100));
        return FromPercent(template, percent, actorFactor, behaviorFlags);

        void ApplyBonus(SacredEquipmentBonus bonus)
        {
            if (bonus.OpponentClass is not (0 or 13) ||
                !SacredEquipmentStatsCalculator.AppliesToHero(bonus.HeroClass, (byte)template.ItemType)) return;
            var value = SacredEquipmentStatsCalculator.ResolveValue(bonus, attributes);
            if (bonus.Code == 812) percent = unchecked((short)(int)(percent + (double)value));
            else if (SacredEquipmentStatsCalculator.Attribute(bonus.Code) is { } attribute)
                attributes = attributes.WithValue(attribute, unchecked((ushort)(int)(attributes[attribute] + value)));
            else if (inherent.Skills.ContainsKey(11) && bonus.Code is 610 or 849) armorLoreBonus += bonus.Value;
        }
    }

    public SacredMovementSpeeds FromPercent(SacredCreatureTemplate template, short percent, float actorFactor = 1, uint behaviorFlags = 0)
    {
        if (!float.IsFinite(actorFactor) || actorFactor < 0) throw new ArgumentOutOfRangeException(nameof(actorFactor));
        // Native x87 rounds this intermediate to a float before multiplying the two base rates.
        var factor = (float)(percent * (double)actorFactor * .01f);
        if ((template.Flags & 1) != 0 && (behaviorFlags & 0x200000) != 0)
        {
            var forcedRun = (int)(200 * (double)balance.MovementScale * factor);
            return new(percent, forcedRun, forcedRun);
        }
        return new(percent, (int)(template.WalkSpeed * (double)balance.MovementScale * factor),
            (int)(template.RunSpeed * (double)balance.MovementScale * factor));
    }

    private static bool Contains(IReadOnlyList<uint> values, uint value)
    {
        foreach (var entry in values) if (entry == value) return true;
        return false;
    }
}
