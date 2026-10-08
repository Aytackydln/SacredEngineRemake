using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.CombatArts;
using Sacred.Core.Pak.Weapon.Bonuses;
using Sacred.Inventory.Items;

namespace Sacred.Inventory.Stats;

/// <summary>Rebuilds a fresh contribution snapshot from concrete items. Conditional bonuses remain
/// in the summary; combat percentages are retained until the native combat calculation is ported.</summary>
internal static class SacredEquipmentStatsCalculator
{
    public static IReadOnlyList<SacredBonusTotal> Summarize(IEnumerable<SacredItemInstance> items) =>
        Array.AsReadOnly(items.SelectMany(item => item.Bonuses)
            .GroupBy(bonus => bonus with { Value = 0 })
            .Select(group => new SacredBonusTotal(group.Key, group.Sum(bonus => (int)bonus.Value)))
            .OrderBy(total => total.Bonus.Code).ThenBy(total => total.Bonus.OpponentClass).ToArray());

    public static SacredActorStats Calculate(IReadOnlyList<SacredItemInstance> items,
        SacredActorStats inherent, byte heroType, SacredCharacterProgression progression)
    {
        var values = new Dictionary<SacredActorStat, double>();
        var skills = new Dictionary<byte, int>();
        var combatArts = new Dictionary<ushort, int>();
        foreach (var item in items)
        {
            var stats = item.BaseStats;
            Add(SacredActorStat.Attack, stats.Attack);
            Add(SacredActorStat.Defense, stats.Defense);
            Add(SacredActorStat.PhysicalProtection, stats.PhysicalProtection);
            Add(SacredActorStat.FireProtection, stats.FireProtection);
            Add(SacredActorStat.MagicProtection, stats.MagicProtection);
            Add(SacredActorStat.PoisonProtection, stats.PoisonProtection);
            var damage = item.Damage;
            Add(SacredActorStat.PhysicalDamageMinimum, damage.PhysicalDamageMinimum);
            Add(SacredActorStat.FireDamageMinimum, damage.FireDamageMinimum);
            Add(SacredActorStat.MagicDamageMinimum, damage.MagicDamageMinimum);
            Add(SacredActorStat.PoisonDamageMinimum, damage.PoisonDamageMinimum);
            Add(SacredActorStat.PhysicalDamageMaximum, damage.PhysicalDamageMaximum);
            Add(SacredActorStat.FireDamageMaximum, damage.FireDamageMaximum);
            Add(SacredActorStat.MagicDamageMaximum, damage.MagicDamageMaximum);
            Add(SacredActorStat.PoisonDamageMaximum, damage.PoisonDamageMaximum);
            foreach (var bonus in item.Bonuses)
            {
                if (!AppliesToHero(bonus.HeroClass, heroType) || bonus.OpponentClass is not (0 or 13)) continue;
                var value = ResolveValue(bonus, inherent);
                if (Attribute(bonus.Code) is { } attribute) Add(attribute, (int)value);
                else if (bonus.Kind == SacredEquipmentBonusKind.Skill)
                {
                    var id = bonus.Code - 599;
                    if (id <= byte.MaxValue && inherent.Skills.ContainsKey((byte)id))
                        skills[(byte)id] = skills.GetValueOrDefault((byte)id) + bonus.Value;
                }
                else if (bonus.Code == 849)
                    foreach (var id in inherent.Skills.Keys) skills[id] = skills.GetValueOrDefault(id) + bonus.Value;
                else if (bonus.Kind == SacredEquipmentBonusKind.CombatArt)
                    AddCombatArt(bonus.Code, bonus.Value);
                else if (bonus.Code is 844 or 845)
                {
                    foreach (var code in inherent.CombatArts.Keys)
                    {
                        // Gold CalcResults treats Combat Healing (1019) as a spell;
                        // the Dwarf does not receive the all-spells (844) bonus.
                        var spell = progression.GetCombatArtKind(code) == SacredCombatArtKind.Spell || code == 1019;
                        if (bonus.Code == 844 ? spell && heroType != 8 : !spell)
                            AddCombatArt(code, bonus.Value);
                    }
                }
                else if (bonus.Code == 811) Add(SacredActorStat.AttackSpeed, (int)value);
                else if (bonus.Code == 812) Add(SacredActorStat.MovementSpeed, (int)value);
                else if (bonus.Code == 813) Add(SacredActorStat.SpellRegeneration, value);
                else if (bonus.Code == 814) Add(SacredActorStat.CombatArtRegeneration, value);
                else if (bonus.Kind == SacredEquipmentBonusKind.DamageBonus)
                {
                    Add(SacredActorStat.PhysicalDamageMinimum + (int)bonus.Element, value);
                    Add(SacredActorStat.PhysicalDamageMaximum + (int)bonus.Element, value);
                }
            }
        }

        // Native Gold clamps effective learned skill ranks to a byte (0x57949E).
        foreach (var id in skills.Keys.ToArray())
            skills[id] = Math.Clamp(inherent.Skills[id] + skills[id], 0, byte.MaxValue) - inherent.Skills[id];
        return new(values, skills, combatArts);

        void Add(SacredActorStat stat, double value) => values[stat] = values.GetValueOrDefault(stat) + value;

        void AddCombatArt(ushort code, short amount)
        {
            if (!inherent.CombatArts.ContainsKey(code)) return;
            var current = combatArts.GetValueOrDefault(code);
            // Gold AddSpellMoveLevel (0x582230): base and bonus are separate bytes.
            // An increment that would overflow the bonus byte is rejected, not clamped.
            var increment = unchecked((ushort)amount);
            if (current + increment <= byte.MaxValue) combatArts[code] = current + increment;
        }
    }

    // Gold 0x568EF0: selector 1..6 scales the inherent attribute; other bytes return
    // the signed magnitude unchanged. Equipment never recursively scales its own bonuses.
    private static float ResolveValue(SacredEquipmentBonus bonus, SacredActorStats inherent)
    {
        SacredActorStat? source = bonus.AttributeSelector switch
        {
            1 => SacredActorStat.Strength, 2 => SacredActorStat.Dexterity, 3 => SacredActorStat.Endurance,
            4 => SacredActorStat.PhysicalRegeneration, 5 => SacredActorStat.MentalRegeneration,
            6 => SacredActorStat.Charisma, _ => null
        };
        return source is { } attribute ? (float)(inherent[attribute] * bonus.Value * 0.01f) : bonus.Value;
    }

    private static SacredActorStat? Attribute(ushort code) => code switch
    {
        815 => SacredActorStat.Strength, 816 => SacredActorStat.Dexterity, 817 => SacredActorStat.Endurance,
        818 => SacredActorStat.PhysicalRegeneration, 819 => SacredActorStat.MentalRegeneration,
        820 => SacredActorStat.Charisma, _ => null
    };

    // Gold 0x568DE0 handles selectors 1..6; all remaining selectors fall through to true.
    private static bool AppliesToHero(byte restriction, byte hero) => restriction switch
    {
        >= 1 and <= 5 => restriction == hero,
        6 => hero is 6 or 7,
        _ => true
    };
}
