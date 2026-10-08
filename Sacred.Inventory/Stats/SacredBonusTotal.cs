using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Inventory.Stats;

/// <summary>Value uses an int so summing signed instance magnitudes cannot overflow a short.</summary>
public readonly record struct SacredBonusTotal(SacredEquipmentBonus Bonus, int Value);
