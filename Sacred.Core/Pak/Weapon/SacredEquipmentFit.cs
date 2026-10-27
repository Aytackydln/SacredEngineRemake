namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredEquipmentFitHero(ushort Strength, ushort Endurance, ushort Dexterity, ushort Charisma, ushort Level);

/// <summary>Gold 0x54ABE0 after its virtual class check. Missing value getter
/// succeeds, class-only requests skip requirements, and skill rank compares
/// only its low byte. The caller must retain class-check flag mutations.</summary>
public static class SacredEquipmentFit
{
    public static bool Requirements(bool classAllowed, bool classOnly, SacredEquipmentInstanceLayout? values,
        SacredEquipmentFitHero hero, Func<byte, byte> skillRank)
    {
        if (!classAllowed) return false;
        if (classOnly || !values.HasValue) return true;
        var item = values.Value;
        if (item.MinimumCharisma > hero.Charisma || item.MinimumDexterity > hero.Dexterity || item.MinimumStrength > hero.Strength ||
            item.MinimumLevel > hero.Level || item.MinimumEndurance > hero.Endurance) return false;
        return item.MinimumSkill == 0 || item.MinimumSkillLevel == 0 || skillRank(item.MinimumSkill) >= item.MinimumSkillLevel;
    }
}
