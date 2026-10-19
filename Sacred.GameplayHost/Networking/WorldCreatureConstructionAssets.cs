using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Items;
using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original inputs shared by new creature factories. No generated item values are cached here.</summary>
public sealed class WorldCreatureConstructionAssets
{
    private WorldCreatureConstructionAssets(string installation)
    {
        Items = ItemsPakArchive.Load(Path.Combine(installation, "pak/Items.pak")).ToDictionary(row => (uint)row.ItemIndex);
        Creatures = CreaturePakArchive.Load(Path.Combine(installation, "pak/Creature.pak"));
        Constructors = SacredCreatureConstructionDefinitions.Load(installation);
        Ambient = SacredAmbientPopulationDefinitions.Load(installation);
        Console.WriteLine($"Creature construction assets loaded: {Items.Count} item definitions, {Creatures.Entries.Count} creature templates.");
    }
    public IReadOnlyDictionary<uint, ItemsPakEntry> Items { get; }
    public CreaturePakArchive Creatures { get; }
    public SacredCreatureConstructionDefinitions Constructors { get; }
    public SacredAmbientPopulationDefinitions Ambient { get; }
    public static WorldCreatureConstructionAssets Load(string installation) => new(installation);
}
