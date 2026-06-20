using Sacred.Core.Pak.Weapon;

namespace Sacred.Engine.Rendering.EquipmentEffects;

public readonly record struct EquipmentEffectAttachment(
    int ModelSliceIndex,
    string ModelName,
    string? RigidAttachBoneName,
    SacredEquipmentDamage Damage,
    float ModelBoundsSize);
