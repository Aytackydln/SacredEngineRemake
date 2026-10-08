using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Pak.Weapon.Descriptions;

/// <summary>Resolves the names of decoded bonuses against global.res.</summary>
internal sealed class SacredEquipmentBonusLabelResolver(GameResStore resources)
{
    private readonly SacredEquipmentDescriptionText _text = new(resources);

    public SacredEquipmentBonusLabel? Resolve(SacredEquipmentBonus bonus)
    {
        return bonus.Kind switch
        {
            SacredEquipmentBonusKind.DamageBonus or SacredEquipmentBonusKind.Damage
                or SacredEquipmentBonusKind.Resistance => ElementLabel(bonus),
            SacredEquipmentBonusKind.Attack => new(_text.Get(1074), AttributeSuffix(bonus.AttributeSelector)),
            SacredEquipmentBonusKind.Defense => new(_text.Get(1075), AttributeSuffix(bonus.AttributeSelector)),
            SacredEquipmentBonusKind.Skill => new(_text.Get(bonus.Code + 8800)),
            SacredEquipmentBonusKind.Named => NamedLabel(bonus.Code),
            SacredEquipmentBonusKind.CombatArt => CombatArtLabel(bonus.Code),
            _ => null
        };
    }

    private SacredEquipmentBonusLabel? NamedLabel(ushort code) =>
        SacredEquipmentBonusResources.Get(code) is { } definition
            ? new(resources.GetString(definition.Key, definition.Key))
            : null;

    private SacredEquipmentBonusLabel? CombatArtLabel(ushort code) =>
        SacredCombatArtResources.GetNameResource(code) is { } resource
            ? new(_text.Get((int)resource))
            : null;

    private SacredEquipmentBonusLabel ElementLabel(SacredEquipmentBonus bonus)
    {
        var elementName = _text.Get(1078 + (int)bonus.Element);
        var label = bonus.Kind switch
        {
            SacredEquipmentBonusKind.Resistance =>
                resources.GetString(ResistanceResource(bonus.Element), $"{elementName} {_text.Get(1077)}"),
            SacredEquipmentBonusKind.DamageBonus => $"{_text.Get(1154)} {elementName}",
            _ => $"{elementName} {_text.Get(DamageResource(bonus.DamageKind))}"
        };

        return new(label, AttributeSuffix(bonus.AttributeSelector));
    }

    private string AttributeSuffix(byte selector)
    {
        var resource = selector switch
        {
            1 => 1090, 2 => 1092, 3 => 1091, 4 => 1093, 5 => 1094, 6 => 1095,
            _ => 0
        };
        return resource == 0 ? "" : $" {_text.Get(resource)}";
    }

    private static int DamageResource(SacredEquipmentDamageKind kind) => kind switch
    {
        SacredEquipmentDamageKind.Weapon => 1087,
        SacredEquipmentDamageKind.Spell => 1088,
        _ => 1076
    };

    private static string ResistanceResource(SacredEquipmentElement element) => element switch
    {
        SacredEquipmentElement.Physical => "INVENTAR_RES_PHYSICAL",
        SacredEquipmentElement.Fire => "INVENTAR_RES_FIRE",
        SacredEquipmentElement.Magic => "INVENTAR_RES_MAGIC",
        SacredEquipmentElement.Poison => "INVENTAR_RES_POISON",
        _ => throw new ArgumentOutOfRangeException(nameof(element))
    };
}
