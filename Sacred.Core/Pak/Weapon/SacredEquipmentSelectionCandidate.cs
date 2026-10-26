using Sacred.Core.GameBin.WeaponModifiers;

namespace Sacred.Core.Pak.Weapon;

/// <summary>A native category-pool member. Filtering requires initialized shared
/// values; building the initial pool uses the original pre-pricing values.
/// Values are not inferred from the presentation classification.</summary>
public readonly record struct SacredEquipmentSelectionCandidate(
    uint ItemType,
    byte Usage,
    byte SetType,
    SacredEquipmentInstanceLayout Values,
    SacredWeaponModifierLayout? Modifier);

/// <summary>Gold 0x5C7FA0 candidate filtering inputs. Level is the original word;
/// native separately clamps its working level to 28. Usage 15 means unrestricted.
/// HeroClass is the native 0..9 selector, not a flag mask. Flags bit 0 excludes
/// authored bonuses; bit 1 bypasses shared spawn/minimum-level bounds.</summary>
public readonly record struct SacredEquipmentSelectionCriteria(
    ushort Level,
    byte Usage = 15,
    uint HeroClass = 0,
    byte Flags = 0,
    bool ExcludeSets = false,
    uint MaximumModifierSelectionValue = 14);
