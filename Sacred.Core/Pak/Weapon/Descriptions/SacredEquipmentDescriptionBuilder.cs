using Sacred.Core.GameBin.Sets;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Details;

namespace Sacred.Core.Pak.Weapon.Descriptions;

/// <summary>Convenience adapter for callers that need a localized tooltip directly from equipment.</summary>
public sealed class SacredEquipmentDescriptionBuilder(GameResStore resources, IReadOnlyList<SacredSetEntry> sets)
{
    private readonly SacredEquipmentDetailsBuilder _details = new(sets);
    private readonly SacredEquipmentDescriptionFormatter _formatter = new(resources);

    public SacredEquipmentDescription Create(SacredEquipment equipment) => _formatter.Format(_details.Create(equipment));
}
