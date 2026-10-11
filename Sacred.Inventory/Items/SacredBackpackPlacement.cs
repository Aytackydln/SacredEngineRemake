namespace Sacred.Inventory.Items;

/// <summary>An item anchor and its authored Weapon.pak footprint, in inventory cells.</summary>
public sealed record SacredBackpackPlacement(SacredItemInstance Item, int X, int Y, int Width, int Height);
