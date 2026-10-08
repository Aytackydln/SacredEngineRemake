using static Sacred.Core.Pak.Weapon.Descriptions.SacredEquipmentDescriptionText;

namespace Sacred.Core.Pak.Weapon.Descriptions;

internal static class SacredEquipmentRequirementDescription
{
    public static SacredEquipmentDescriptionSection? Create(SacredEquipmentRequirements requirements,
        SacredEquipmentDescriptionText text)
    {
        var fields = new List<SacredEquipmentDescriptionField>();
        AddMinimum(1070, "Level", requirements.Level);
        AddMinimum(1090, "Strength", requirements.Strength);
        AddMinimum(1092, "Dexterity", requirements.Dexterity);
        AddMinimum(1095, "Charisma", requirements.Charisma);
        AddMinimum(1091, "Endurance", requirements.Endurance);

        if (requirements.Skill != 0 && requirements.SkillLevel != 0)
        {
            fields.Add(new(text.Get(requirements.Skill + 9399, $"Skill {requirements.Skill}"),
                Number(requirements.SkillLevel)));
        }

        return fields.Count == 0 ? null : new("Minimum requirements", fields);

        void AddMinimum(int key, string fallback, int value)
        {
            if (value != 0)
                fields.Add(new(text.Get(key, fallback), Number(value)));
        }
    }
}
