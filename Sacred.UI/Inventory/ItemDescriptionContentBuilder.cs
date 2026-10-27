using System.Collections.Generic;
using System.Globalization;
using Sacred.Core.GameBin.Sets;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Descriptions;
using Sacred.Core.Pak.Weapon.Details;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Inventory.Items;

namespace Sacred.UI.Inventory;

/// <summary>Projects concrete item values into the native description's localized, colored lines.
/// Does not calculate hero damage, expected protection, merchant prices or equip eligibility.</summary>
public sealed class ItemDescriptionContentBuilder(GameResStore resources, IReadOnlyList<SacredSetEntry> sets,
    ItemDescriptionEquipmentRules? equipmentRules = null)
{
    /// <summary>The piece name shared by hover headings, drop history and status text, without socket counts.</summary>
    public string GetName(SacredItemInstance item) =>
        GetName(item, new SacredEquipmentDetailsBuilder(sets).Create(item.Definition).Identity.Set);

    private string GetName(SacredItemInstance item, SacredEquipmentSetReference? set)
    {
        var definition = item.Definition;
        var rarity = item.ResolvedFlags is { } flags ? (flags >> 16) & 15 : definition.Classification.RarityTierCode;
        if (set is null && rarity != 15 && item.ResolvedName is { } resolved)
            return resolved.Text;

        var authoredName = SacredEquipmentNameLookup.Resolve(resources, definition.Item.ModelDesc.ResourceId);
        return string.IsNullOrEmpty(authoredName) ? Get(definition.IdemId, definition.Name) : authoredName;
    }

    public IReadOnlyList<ItemDescriptionLine> Create(SacredItemInstance item)
    {
        var lines = new List<ItemDescriptionLine>();
        var definition = item.Definition;
        var classification = item.NativeValues is { } values
            ? SacredEquipmentClassification.FromBytes((byte)values.Flags, values.EquipmentTypeCode, (byte)(values.Flags >> 16))
            : definition.Classification;
        var flags = item.ResolvedFlags ?? ((uint)classification.CharacterClassMaskCode |
            ((uint)classification.EquipmentTypeCode << 8) | ((uint)classification.RarityAndClassFlags << 16));
        var set = new SacredEquipmentDetailsBuilder(sets).Create(definition).Identity.Set;
        var name = GetName(item, set);
        var slots = item.SlotTypes;
        var available = 0;
        var occupied = 0;
        for (var index = 0; index < 8; index++)
        {
            if (slots[index] != 0) available++;
            if (slots[index] is >= 1 and <= 4 && index < item.SocketRecords.Count && item.SocketRecords[index].ItemType != 0)
                occupied++;
        }
        if (available > 0) name += $" [{Number(occupied)}/{Number(available)}]";
        lines.Add(new(name, ItemDescriptionColors.Name(flags, definition.SetType, definition.Item.ModelDesc.Category))
        { IsHeading = true, IconSelector = ItemDescriptionEquipmentRules.DifficultyEmblem(flags) });
        if (set is { } membership)
        {
            var title = resources.GetString($"SetName{membership.Index}", "");
            if (membership.NameResourceId is { } resourceId) title = resources.Strings.GetValueOrDefault(resourceId, title);
            if (!string.IsNullOrEmpty(title)) lines.Add(new(title, ItemDescriptionColors.Set) { IsHeading = true });
        }
        var skill = ItemDescriptionEquipmentRules.WeaponSkill(definition);
        if (skill != 0) lines.Add(new(Get((uint)skill + 9399, ""), ItemDescriptionColors.Text));
        if (equipmentRules?.IsTwoHanded(definition) == true)
            lines.Add(new(resources.GetString("EWX_TWOHANDED", "Two-handed"), ItemDescriptionColors.Text));
        AddClasses(lines, classification.EffectiveCharacterClassMask);
        Add(lines, Get(1055, "Price"), item.Price);
        AddDamageTotal(lines, item.Damage);
        Add(lines, Get(1070, "Level"), item.Level);
        if (item.BaseStats.Attack != 0) Add(lines, Get(1074, "Attack"), item.BaseStats.Attack);
        if (item.BaseStats.Defense != 0) Add(lines, Get(1075, "Defense"), item.BaseStats.Defense);
        AddElements(lines, item.Damage, item.BaseStats);
        AddRequirements(lines, item.Requirements);
        foreach (var bonus in item.Bonuses)
        {
            foreach (var field in SacredEquipmentBonusDescriptions.Format([bonus], resources))
                lines.Add(new(string.IsNullOrEmpty(field.Value) ? field.Label : $"{field.Label}: {field.Value}",
                    ItemDescriptionColors.Modifier(bonus.Code)));
        }
        return lines;
    }

    private void AddClasses(List<ItemDescriptionLine> lines, SacredCharacterClassMask classes)
    {
        if (classes is SacredCharacterClassMask.None or SacredCharacterClassMask.AllKnown) return;
        for (var bit = 0; bit < 8; bit++)
        {
            if (((byte)classes & (1 << bit)) == 0) continue;
            var key = bit < 6 ? bit + 1 : bit + 2;
            var name = Get((uint)key, ((SacredCharacterClassMask)(1 << bit)).ToString());
            var flag = bit < 6 ? 1u << bit : 1u << (bit + 16);
            lines.Add(new(name, ItemDescriptionColors.Name(flag, 0, default)));
        }
    }

    private void AddDamageTotal(List<ItemDescriptionLine> lines, SacredEquipmentDamage damage)
    {
        var minimum = damage.PhysicalDamageMinimum + damage.FireDamageMinimum + damage.MagicDamageMinimum + damage.PoisonDamageMinimum;
        var maximum = damage.PhysicalDamageMaximum + damage.FireDamageMaximum + damage.MagicDamageMaximum + damage.PoisonDamageMaximum;
        if (minimum != 0 || maximum != 0)
            lines.Add(new($"{Get(1176, "Damage")}: {Range(minimum, maximum)}", ItemDescriptionColors.Damage));
    }

    private void AddElements(List<ItemDescriptionLine> lines, SacredEquipmentDamage damage, SacredEquipmentBaseStats stats)
    {
        AddElement(0, damage.PhysicalDamageMinimum, damage.PhysicalDamageMaximum, stats.PhysicalProtection, "Physical", "INVENTAR_RES_PHYSICAL");
        AddElement(1, damage.FireDamageMinimum, damage.FireDamageMaximum, stats.FireProtection, "Fire", "INVENTAR_RES_FIRE");
        AddElement(2, damage.MagicDamageMinimum, damage.MagicDamageMaximum, stats.MagicProtection, "Magic", "INVENTAR_RES_MAGIC");
        AddElement(3, damage.PoisonDamageMinimum, damage.PoisonDamageMaximum, stats.PoisonProtection, "Poison", "INVENTAR_RES_POISON");
        void AddElement(uint element, int minimum, int maximum, int protection, string fallback, string resistanceKey)
        {
            var color = ItemDescriptionColors.Modifier((ushort)(801 + element));
            if (minimum != 0 || maximum != 0)
                lines.Add(new($"{Get(1078 + element, fallback)}: {Range(minimum, maximum)}", color));
            if (protection != 0)
                lines.Add(new($"{resources.GetString(resistanceKey, fallback + " Resistance")}: {Number(protection)}", color));
        }
    }

    private void AddRequirements(List<ItemDescriptionLine> lines, SacredEquipmentRequirements requirements)
    {
        Minimum("INVENTAR_MIN_LEVEL", "Minimum Level", requirements.Level);
        Minimum("INVENTAR_MIN_FORCE", "Minimum Strength", requirements.Strength);
        Minimum("INVENTAR_MIN_DEX", "Minimum Dexterity", requirements.Dexterity);
        Minimum("INVENTAR_MIN_CHARISMA", "Minimum Charisma", requirements.Charisma);
        Minimum("INVENTAR_MIN_END", "Minimum Endurance", requirements.Endurance);
        if (requirements.Skill != 0 && requirements.SkillLevel != 0)
            Add(lines, $"{Get(1108, "Minimum")} {Get((uint)requirements.Skill + 9399, "Skill")}", requirements.SkillLevel);
        void Minimum(string key, string fallback, int amount)
        {
            if (amount != 0) Add(lines, resources.GetString(key, fallback), amount);
        }
    }

    private string Get(uint key, string fallback) => resources.GetString(Number(key), fallback);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Range(int minimum, int maximum) => minimum == maximum ? Number(minimum) : $"{Number(minimum)}-{Number(maximum)}";
    private static void Add(List<ItemDescriptionLine> lines, string label, long value) =>
        lines.Add(new($"{label}: {Number(value)}", ItemDescriptionColors.Text));
}
