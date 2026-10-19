using System.Collections.Frozen;
using System.IO;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Items;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;

namespace Sacred.Engine.Multiplayer;

/// <summary>Original Creature.pak ItemType associations synthesized by the native creature loader.</summary>
internal static class GameServerCreatureClasses
{
    public static FrozenDictionary<uint, SacredItemCategory> ReadObjectCategories(string gameDirectory) =>
        ItemsPakArchive.Load(Path.Combine(gameDirectory, "pak", "Items.pak"))
            .ToFrozenDictionary(item => (uint)item.ItemIndex, item => item.ModelDesc.Category);
    public static FrozenDictionary<uint, uint> ReadObjectResources(string gameDirectory) =>
        ItemsPakArchive.Load(Path.Combine(gameDirectory, "pak", "Items.pak"))
            .ToFrozenDictionary(item => (uint)item.ItemIndex, item => item.ModelDesc.ResourceId);
    public static FrozenDictionary<uint, SacredCreatureClass> Read(string gameDirectory) =>
        CreaturePakArchive.Load(Path.Combine(gameDirectory, "pak", "Creature.pak")).ByItemType
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Class);
}
