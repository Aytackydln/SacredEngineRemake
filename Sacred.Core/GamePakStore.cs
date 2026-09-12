using System.Collections.Frozen;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Core;

public class GamePakStore(
    FrozenDictionary<uint, SacredEquipment> weapons,
    FrozenDictionary<ushort, ItemsPakEntry> items
)
{
    public FrozenDictionary<uint, SacredEquipment> Weapons { get; } = weapons;

    public FrozenDictionary<ushort, ItemsPakEntry> Items { get; } = items;
}