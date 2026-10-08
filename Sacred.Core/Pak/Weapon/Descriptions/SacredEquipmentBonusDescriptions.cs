using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Pak.Weapon.Descriptions;

/// <summary>Formats decoded bonuses, with convenience adapters for raw authored slots.</summary>
public static class SacredEquipmentBonusDescriptions
{
    public static IReadOnlyList<SacredEquipmentDescriptionField> Format(
        IReadOnlyList<SacredEquipmentBonus> bonuses, GameResStore resources)
    {
        var formatter = new SacredEquipmentBonusDescriptionFormatter(resources);
        var fields = new List<SacredEquipmentDescriptionField>(bonuses.Count);
        foreach (var bonus in bonuses)
        {
            if (formatter.Format(bonus) is { } field)
                fields.Add(field);
        }

        return fields;
    }

    public static IReadOnlyList<SacredEquipmentDescriptionField> Create(SacredEquipment equipment, GameResStore resources) =>
        Format(SacredEquipmentBonusDecoder.Decode(equipment), resources);

    public static SacredEquipmentDescriptionField? Create(uint group, ushort type, short value, GameResStore resources) =>
        new SacredEquipmentBonusDescriptionFormatter(resources).Format(SacredEquipmentBonusDecoder.Decode(group, type, value));
}
