using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.GameServer.Auth.Hero;

namespace Sacred.GameServer.Tests;

/// <summary>
/// Item footprints from the game's tables against real saves: every anchor cell of a C1/C6 inventory carries the item's
/// width/height in word +4 (bytes 1 and 2), so the resolver must return exactly those for the item's resource. Needs the game
/// (SACRED_GAME_DIR) and saves (SACRED_HERO_SAVES); inconclusive without them.
/// </summary>
[TestClass]
public class ItemFootprintTests
{
    internal static ItemFootprints? Tables()
    {
        var folder = Environment.GetEnvironmentVariable("SACRED_GAME_DIR") ?? @"B:\Spiele\GOG Games\sacred gold GOG";
        return Directory.Exists(folder) ? ItemFootprints.Load(folder, out _) : null;
    }

    [TestMethod]
    public void FootprintsMatchSavedInventories()
    {
        var tables = Tables();
        if (tables is null) Assert.Inconclusive("no game directory (set SACRED_GAME_DIR)");
        var folder = Environment.GetEnvironmentVariable("SACRED_HERO_SAVES") ?? @"B:\sharedstash-backup";
        var saves = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.pax", SearchOption.AllDirectories).ToArray() : [];
        if (saves.Length == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");

        var seen = new HashSet<string>();
        int checkedCells = 0, files = 0;
        var resources = new HashSet<uint>();
        var sizes = new SortedDictionary<string, int>();
        var mismatches = new List<string>();
        foreach (var path in saves)
        {
            var file = File.ReadAllBytes(path);
            if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)))) continue;
            var d = HeroWriterTests.Decode(file);
            files++;
            var resourceOf = new Dictionary<uint, uint>();
            foreach (var (resource, data) in d.Records) resourceOf[SavedObjects.Reference(data)] = resource;
            foreach (var inventory in new[] { d.Inventory, d.Shared })
            {
                if (inventory is null || inventory.Length < SacredSavedInventoryLayout.CellsOffset + SacredSavedInventoryLayout.CellCount * SacredSavedInventoryLayout.CellSize) continue;
                for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
                {
                    var at = inventory.AsSpan(SacredSavedInventoryLayout.CellsOffset + cell * SacredSavedInventoryLayout.CellSize);
                    var reference = BinaryPrimitives.ReadUInt32LittleEndian(at);
                    if (reference == 0 || !resourceOf.TryGetValue(reference, out var resource)) continue;
                    var word = BinaryPrimitives.ReadUInt32LittleEndian(at[4..]);
                    var (w, h) = tables!.Get(resource);
                    checkedCells++;
                    resources.Add(resource);
                    sizes[$"{w}x{h}"] = sizes.GetValueOrDefault($"{w}x{h}") + 1;
                    if ((word >> 8 & 0xFF) != w || (word >> 16 & 0xFF) != h)
                        mismatches.Add($"{Path.GetFileName(path)} cell {cell}: resource 0x{resource:X} saved {word >> 8 & 0xFF}x{word >> 16 & 0xFF}, tables {w}x{h}");
                }
            }
        }
        Console.WriteLine($"{files} saves, {checkedCells} anchor cells, {resources.Count} distinct resources, {mismatches.Count} mismatches, {tables!.FromRows} resources sized by Weapon.pak rows; sizes {string.Join(", ", sizes.Select(p => $"{p.Key}: {p.Value}"))}");
        Assert.IsTrue(checkedCells > 0, "the saves hold inventory items");
        Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches.Distinct().Take(40)));
    }

    [TestMethod]
    public void FixedAndOutOfRangeFootprints()
    {
        var tables = Tables();
        if (tables is null) Assert.Inconclusive("no game directory (set SACRED_GAME_DIR)");
        Assert.AreEqual((2, 2), tables!.Get(0xFA7));
        Assert.AreEqual((1, 1), tables.Get(ItemFootprints.ResourceCount));
        Assert.AreEqual(0x00020201u, tables.AnchorWord(0xFA7));
    }
}
