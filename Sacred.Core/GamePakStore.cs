using System.Collections.Frozen;
using Sacred.Core.Items;
using Sacred.Core.Texture;
using Sacred.Core.Weapon;

namespace Sacred.Core;

public class GamePakStore(
    FrozenDictionary<uint, SacredEquipment> weapons,
    FrozenDictionary<ushort, ItemsPakEntry> items,
    FrozenDictionary<string, SacredTextureInfo> textures
)
{
    public FrozenDictionary<uint, SacredEquipment> Weapons { get; } = weapons;

    public FrozenDictionary<ushort, ItemsPakEntry> Items { get; } = items;

    public FrozenDictionary<string, SacredTextureInfo> Textures { get; } = textures;
}