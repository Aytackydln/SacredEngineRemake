using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Details;
using static Sacred.Core.Pak.Weapon.Descriptions.SacredEquipmentDescriptionText;

namespace Sacred.Core.Pak.Weapon.Descriptions;

/// <summary>Localizes structured equipment facts into display sections and fields.</summary>
public sealed class SacredEquipmentDescriptionFormatter(GameResStore resources)
{
    private readonly SacredEquipmentDescriptionText _text = new(resources);
    private readonly SacredEquipmentIdentityDescription _identity = new(resources);

    public SacredEquipmentDescription Format(SacredEquipmentDetails details)
    {
        var sections = new List<SacredEquipmentDescriptionSection>
        {
            _identity.Create(details.Identity),
            SacredEquipmentBaseValueDescription.Create(details.BaseValues, _text)
        };

        if (SacredEquipmentRequirementDescription.Create(details.Requirements, _text) is { } requirements)
            sections.Add(requirements);

        var bonuses = SacredEquipmentBonusDescriptions.Format(details.Bonuses, resources);
        if (bonuses.Count > 0)
            sections.Add(new("Base bonuses", bonuses));

        var name = resources.GetString(Number(details.Identity.NameResourceKey), details.Identity.AuthoredName);
        return new(name, sections);
    }
}
