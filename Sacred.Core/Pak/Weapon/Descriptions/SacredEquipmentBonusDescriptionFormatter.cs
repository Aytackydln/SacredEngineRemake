using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Pak.Weapon.Descriptions;

internal sealed class SacredEquipmentBonusDescriptionFormatter(GameResStore resources)
{
    private readonly SacredEquipmentBonusLabelResolver _resolver = new(resources);
    private readonly SacredEquipmentDescriptionText _text = new(resources);

    public SacredEquipmentDescriptionField? Format(SacredEquipmentBonus bonus)
    {
        if (_resolver.Resolve(bonus) is not { } label)
            return null;

        return new(ApplyRestrictions(label.Text, bonus), FormatValue(bonus, label.Suffix));
    }

    private static string FormatValue(SacredEquipmentBonus bonus, string suffix)
    {
        if (bonus.Unit == SacredEquipmentBonusUnit.None)
            return "";

        var unit = bonus.Unit == SacredEquipmentBonusUnit.Percent ? "%" : "";
        return $"{SacredEquipmentDescriptionText.Signed(bonus.Value)}{unit}{suffix}";
    }

    private string ApplyRestrictions(string label, SacredEquipmentBonus bonus)
    {
        if (bonus.HeroClass != 0)
            label = $"{_text.Get(bonus.HeroClass == 15 ? 1183 : bonus.HeroClass)}: {label}";

        if (OpponentName(bonus.OpponentClass) is { } opponent)
            label = $"{label} ({_text.Get(1161)} {opponent})";

        return label;
    }

    private string? OpponentName(byte opponentClass)
    {
        if (opponentClass is 0 or 13)
            return null;
        if (SacredCombatArtResources.GetOpponentResource(opponentClass) is { } resource)
            return _text.Get((int)resource);

        return opponentClass switch
        {
            128 => resources.GetString("RES_GEG_FERNK", "Ranged fighters"),
            129 => resources.GetString("RES_GEG_REITER", "Mounted"),
            _ => null
        };
    }
}
