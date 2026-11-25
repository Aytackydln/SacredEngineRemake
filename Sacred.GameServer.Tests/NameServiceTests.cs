using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Data;

namespace Sacred.GameServer.Tests;

/// <summary>
/// The game's names against the real install (SACRED_GAME_DIR, English) and real saves (SACRED_HERO_SAVES); inconclusive without
/// them. The expected names were picked from the saves: a Vampiress (Vampirism, "Turn into Vampire"), a Dwarf (Weapon Technology,
/// "Flame Thrower"), a Daemon ("Blazing Disc") and their items.
/// </summary>
[TestClass]
public class NameServiceTests
{
    private static readonly Lazy<NameService?> Loaded = new(() =>
    {
        var folder = Environment.GetEnvironmentVariable("SACRED_GAME_DIR") ?? @"B:\Spiele\GOG Games\sacred gold GOG";
        return Directory.Exists(folder) ? NameService.Load(folder, out _, "us") : null;
    });

    /// <summary>The names of the test install (English), or null without it.</summary>
    internal static NameService? Shared => Loaded.Value;

    private static NameService Names()
    {
        if (Shared is not { } names) { Assert.Inconclusive("no game directory (set SACRED_GAME_DIR)"); throw new InvalidOperationException(); }
        if (names.Language != "us") Assert.Inconclusive("the expected names are the English ones");
        return names;
    }

    [TestMethod]
    public void KnownNamesResolve()
    {
        var names = Names();
        // Creatures: Items.pak types (hero classes 1..9, monsters, the save's horse).
        Assert.AreEqual("Seraphim", names.Creature(1));
        Assert.AreEqual("Dwarf", names.Creature(8));
        Assert.AreEqual("Skeleton", names.Creature(32));
        Assert.AreEqual("Orc Warrior", names.Creature(38));
        Assert.AreEqual("Horse", names.Creature(551));
        // Items: base names of the types, and an item's own generated name from its name word.
        Assert.AreEqual("Lesser Healing Potion", names.Item(5171));
        Assert.AreEqual("Longbow", names.Item(2501));
        Assert.AreEqual("Shortsword", names.Item(7906));
        Assert.AreEqual("Decaying Bow of Oblivion", names.Item(2501, 0xCF3C2E77));
        Assert.AreEqual("Worn Greaves of Pain", names.Item(1966, 0xE8F42995));
        Assert.IsNull(names.Item(1966), "a type without its own text has no base name");
        // Skills (slot ids) and combat arts (art record ids: spells 1.., special moves 1000..).
        Assert.AreEqual("Weapon Lore", names.Skill(2));
        Assert.AreEqual("Vampirism", names.Skill(20));
        Assert.AreEqual("Weapon Technology", names.Skill(29));
        Assert.AreEqual("Turn into Vampire", names.CombatArt(1022));
        Assert.AreEqual("Flame Thrower", names.CombatArt(1085));
        Assert.AreEqual("Blazing Disc", names.CombatArt(60));
        Assert.AreEqual("Dwarven Steel", names.CombatArt(59));
        // Nothing for empty or unknown ids.
        Assert.IsNull(names.Skill(0));
        Assert.IsNull(names.Skill(34), "+++EMPTY+++ slots");
        Assert.IsNull(names.CombatArt(0));
        Assert.IsNull(names.CombatArt(9999));
        Assert.IsNull(names.Creature(0));
        Assert.IsNull(names.Item(0x7FFFFFF));
    }

    [TestMethod]
    public void SavedHeroesResolveCompletely()
    {
        var names = Names();
        var saves = HeroWriterTests.Saves().ToArray();
        if (saves.Length == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        int skills = 0, arts = 0, items = 0;
        var missing = new List<string>();
        foreach (var path in saves)
        {
            var d = HeroWriterTests.Decode(File.ReadAllBytes(path));
            var values = SacredSavedHero.FromPayload(d.Hero, d.Version).Creature.Values;
            foreach (var id in values.Block08.Slice(28, 8))
                if (id != 0) { skills++; if (names.Skill(id) is null) missing.Add($"{Path.GetFileName(path)}: skill {id}"); }
            for (var i = 0; i < values.Count22; i++)
            {
                var id = BinaryPrimitives.ReadUInt16LittleEndian(values.GetRecord22(i)[4..]);
                arts++;
                if (names.CombatArt(id) is null) missing.Add($"{Path.GetFileName(path)}: art {id}");
            }
            foreach (var (type, data) in d.Records)
            {
                if (!SavedObjects.IsItem(data)) continue;
                // Saved objects keep the name word (native +0x3C) at +51 (SacredSavedObjectBaseLayout.Word3C).
                var word = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(51));
                items++;
                if (names.Item(type, word) is null) missing.Add($"{Path.GetFileName(path)}: item {type} name word 0x{word:X8}");
            }
        }
        Console.WriteLine($"{saves.Length} saves: {skills} skills, {arts} combat arts, {items} items");
        Assert.IsTrue(skills > 0 && arts > 0 && items > 0);
        Assert.AreEqual(0, missing.Count, string.Join("\n", missing.Take(40)));
    }

    [TestMethod]
    public void InstanceNameReadsTheBaseStateWord()
    {
        var body = new byte[41];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 0xFEEDF00D);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(37), 0xCF3C2E77);
        Assert.AreEqual(0xCF3C2E77u, NameService.InstanceName(body));
        body[0] = 0;
        Assert.AreEqual(0u, NameService.InstanceName(body), "no base state marker");
        Assert.AreEqual(0u, NameService.InstanceName(body.AsSpan(0, 40)));
    }

    [TestMethod]
    public void MissingGameDirectoryIsAnError()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sacred-names-" + Guid.NewGuid().ToString("N"));
        Assert.IsNull(NameService.Load(folder, out var error));
        Assert.IsNotNull(error);
    }
}
