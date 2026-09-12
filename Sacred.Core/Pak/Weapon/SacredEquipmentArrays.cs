using System.Runtime.CompilerServices;

namespace Sacred.Core.Pak.Weapon;

// Fixed arrays in sWeaponInfoShared/sWeaponInfo, from the matching 1.0 demo.
[InlineArray(6)] public struct SacredEquipmentDefaultSlots { private uint _first; }
[InlineArray(8)] public struct SacredEquipmentSlotTypes { private byte _first; }
[InlineArray(8)] public struct SacredEquipmentBonusTypes { private ushort _first; }
[InlineArray(8)] public struct SacredEquipmentBonusGroups { private uint _first; }
[InlineArray(8)] public struct SacredEquipmentBonusValues { private short _first; }
[InlineArray(7)] public struct SacredEquipmentLegacyRequirements { private byte _first; }
