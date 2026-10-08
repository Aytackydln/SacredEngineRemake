using Sacred.Core.Pak.Weapon.Details;
using static Sacred.Core.Pak.Weapon.Descriptions.SacredEquipmentDescriptionText;

namespace Sacred.Core.Pak.Weapon.Descriptions;

internal static class SacredEquipmentBaseValueDescription
{
    public static SacredEquipmentDescriptionSection Create(SacredEquipmentBaseValues values, SacredEquipmentDescriptionText text)
    {
        var fields = new List<SacredEquipmentDescriptionField>();
        if (values.HasDamage)
            AddDamage(fields, values, text);

        var stats = values.Stats;
        if (stats.Attack != 0)
            fields.Add(new(text.Get(1074, "Attack"), Number(stats.Attack)));
        if (stats.Defense != 0)
            fields.Add(new(text.Get(1075, "Defense"), Number(stats.Defense)));

        if (values.HasProtection)
            AddProtection(fields, stats, text);

        fields.Add(new($"{text.Get(1168, "Movement")} (base speed bonus)", Signed(values.MovementBonus)));
        return new("Base values", fields);
    }

    private static void AddDamage(List<SacredEquipmentDescriptionField> fields, SacredEquipmentBaseValues values,
        SacredEquipmentDescriptionText text)
    {
        fields.Add(new($"{text.Get(1176, "Damage")} (base)", Range(values.MinimumDamage, values.MaximumDamage)));
        var damage = values.Damage;
        AddElement(1078, damage.PhysicalDamageMinimum, damage.PhysicalDamageMaximum);
        AddElement(1079, damage.FireDamageMinimum, damage.FireDamageMaximum);
        AddElement(1080, damage.MagicDamageMinimum, damage.MagicDamageMaximum);
        AddElement(1081, damage.PoisonDamageMinimum, damage.PoisonDamageMaximum);

        void AddElement(int key, int min, int max)
        {
            if (min != 0 || max != 0)
                fields.Add(new(text.Get(key, "Damage"), Range(min, max)));
        }
    }

    private static void AddProtection(List<SacredEquipmentDescriptionField> fields, SacredEquipmentBaseStats stats,
        SacredEquipmentDescriptionText text)
    {
        fields.Add(new($"{text.Get(1177, "Expected Protection")} (base total)", Number(stats.TotalProtection)));
        fields.Add(new(text.Get(1078, "Physical"), Number(stats.PhysicalProtection)));
        fields.Add(new(text.Get(1079, "Fire"), Number(stats.FireProtection)));
        fields.Add(new(text.Get(1080, "Magic"), Number(stats.MagicProtection)));
        fields.Add(new(text.Get(1081, "Poison"), Number(stats.PoisonProtection)));
    }
}
