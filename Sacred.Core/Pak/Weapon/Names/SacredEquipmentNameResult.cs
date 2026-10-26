namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>cObject +0x3C: a numeric resource or a hash tagged with bit 31.
/// This name is separate from the item's visual and value-record resources.</summary>
public sealed record SacredEquipmentNameResult(uint NativeReference, string Text, string? GeneratedKey = null);
