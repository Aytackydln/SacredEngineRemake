using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Effects;

public readonly record struct EquipmentEffectAttachment(
    int ModelSliceIndex,
    string ModelName,
    string? RigidAttachBoneName,
    SacredEquipmentDamage Damage,
    float ModelBoundsSize)
{
    public uint ItemId { get; init; }
    public uint BaseItemId { get; init; }
    public SacredEquipmentBonusTypes BonusTypes { get; init; }
    public SacredEquipmentBonusGroups BonusGroups { get; init; }
    public SacredEquipmentType EquipmentType { get; init; }
    public byte ItemEffectSelector { get; init; }
}
