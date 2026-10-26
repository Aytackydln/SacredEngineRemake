using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Resolved original services used by item naming; no runtime pointers.</summary>
public sealed record SacredEquipmentNameContext(SacredEquipmentNameRules Rules, SacredEquipmentClassRules Classes,
    GameResStore Resources, SacredEquipmentBonusBalance Curves, float LevelFactorHundred,
    Func<uint, SacredEquipmentLayout?> Shared, Func<uint, SacredItemCategory> Category,
    Func<uint, uint> ResourceId, Func<uint, int?> ModifierType, Func<uint, string?> SymbolicName);
