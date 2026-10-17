using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x122 has an outer inventory ID and the shared Gold file/network stream. No reference or owner changes are applied.</summary>
public sealed record GoldInventoryEvent(GoldMessage Message, uint InventoryId, SacredSavedInventory Inventory) : GoldGameEvent(Message);
