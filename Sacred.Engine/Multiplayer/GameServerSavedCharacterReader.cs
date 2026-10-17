using System.Collections.Frozen;
using System.IO;
using System.Linq;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pax.Hero;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerSavedCharacterReader
{
    public static SacredSavedCharacterRecords Read(string gameDirectory, int selection)
        => Read(SacredHeroPaxArchive.ReadSelected(gameDirectory, selection), gameDirectory);

    public static SacredSavedCharacterRecords Read(SacredHeroPaxArchive archive, string gameDirectory)
    {
        var pak = Path.Combine(gameDirectory, "pak");
        var items = ItemsPakArchive.Load(Path.Combine(pak, "Items.pak")).ToFrozenDictionary(item => item.ItemIndex);
        var categories = items.ToDictionary(item => (uint)item.Key, item => (byte)item.Value.ModelDesc.Category);
        // Reuse native base-visual inheritance in equipment record order, including synthesized descriptors.
        foreach (var equipment in WeaponPakParser.Parse(Path.Combine(pak, "Weapon.pak"), items))
            categories[equipment.IdemId] = (byte)equipment.Item.ModelDesc.Category;
        byte Resolve(uint resource) => categories.TryGetValue(resource, out var category) ? category :
            throw new InvalidDataException($"Saved object resource {resource} has no installed definition.");
        return SacredSavedCharacterRecords.FromArchive(archive, Resolve, 64 * 1024 * 1024, (int)GoldReferenceCodec.ReferenceLimit - 1);
    }
}
