using System.Buffers.Binary;
using Sacred.GameServer.Auth;
using Sacred.GameServer.Auth.Hero;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>
/// Item and hero plausibility (Auth/ItemGuard.cs, Auth/HeroGuard.cs). Every item of the real saves (SACRED_HERO_SAVES) must
/// pass, with and without the game's tables (SACRED_GAME_DIR); forged variants of them must not.
/// </summary>
[TestClass]
public class ItemGuardTests
{
    private const int Records = GoldItemStateLayout.RecordsOffset, Bytes = 130;

    /// <summary>Live bodies of every item of every distinct save, as the client uploads them.</summary>
    internal static List<byte[]> SavedItems()
    {
        var folder = Environment.GetEnvironmentVariable("SACRED_HERO_SAVES") ?? @"B:\sharedstash-backup";
        var result = new List<byte[]>();
        if (!Directory.Exists(folder)) return result;
        var seen = new HashSet<string>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.pax", SearchOption.AllDirectories))
        {
            var file = File.ReadAllBytes(path);
            if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)))) continue;
            result.AddRange(HeroWriterTests.Upload(HeroWriterTests.Decode(file)).Bodies.Values);
        }
        return result;
    }

    private static int Count(byte[] body) => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(GoldItemStateLayout.CountOffset));
    private static Span<byte> Own(byte[] body) => body.AsSpan(Records + Count(body) * Bytes, Bytes);

    /// <summary>The body with one more socket record (a copy of <paramref name="socket"/>) in front of the item's own block.</summary>
    internal static byte[] WithSocket(byte[] body, ReadOnlySpan<byte> socket)
    {
        var count = Count(body);
        var grown = new byte[body.Length + Bytes];
        body.AsSpan(0, Records + count * Bytes).CopyTo(grown);
        socket.CopyTo(grown.AsSpan(Records + count * Bytes));
        body.AsSpan(Records + count * Bytes).CopyTo(grown.AsSpan(Records + (count + 1) * Bytes));
        BinaryPrimitives.WriteUInt16LittleEndian(grown.AsSpan(GoldItemStateLayout.CountOffset), (ushort)(count + 1));
        return grown;
    }

    [TestMethod]
    public void EveryRealItemIsPlausible()
    {
        var items = SavedItems();
        if (items.Count == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var tables = ItemFootprintTests.Tables();
        var refused = new List<string>();
        var socketed = 0;
        foreach (var body in items)
        {
            if (Count(body) > 0) socketed++;
            if (ItemGuard.Check(body, null) is { } a) refused.Add(a);
            if (tables is not null && ItemGuard.Check(body, tables) is { } b) refused.Add(b);
        }
        Assert.AreEqual(0, refused.Count, string.Join("\n", refused.Take(20)));
        Assert.IsTrue(socketed > 0, "the saves include socketed items");
        if (tables is null) Assert.Inconclusive($"{items.Count} items pass the structural checks; no game directory for the table checks (set SACRED_GAME_DIR)");
    }

    [TestMethod]
    public void ForgedItemsAreRefused()
    {
        var items = SavedItems();
        if (items.Count == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var gem = items.First(b => Count(b) > 0);
        var socket = gem.AsSpan(Records, Bytes).ToArray();

        // A socket the item does not have: an item without open sockets gets a gem record.
        var closed = items.First(b => Count(b) == 0 && !Own(b)[12..20].ToArray().Any(t => t is >= 1 and <= 4));
        Assert.IsNull(ItemGuard.Check(closed, null));
        StringAssert.Contains(ItemGuard.Check(WithSocket(closed, socket), null), "sockets");

        // Nine socket records are never possible.
        var many = closed;
        for (var i = 0; i < 9; i++) many = WithSocket(many, socket);
        StringAssert.Contains(ItemGuard.Check(many, null), "9 socket records");

        var tables = ItemFootprintTests.Tables();
        if (tables is null) Assert.Inconclusive("no game directory for the table checks (set SACRED_GAME_DIR)");
        Assert.IsNull(ItemGuard.Check(gem, tables));

        // The values of another item put on this one (a weak item carrying a strong item's block).
        var other = items.First(b => BinaryPrimitives.ReadUInt32LittleEndian(Own(b)) != BinaryPrimitives.ReadUInt32LittleEndian(Own(closed)) &&
            BinaryPrimitives.ReadUInt32LittleEndian(Own(b)) != tables.Base(BinaryPrimitives.ReadUInt32LittleEndian(closed.AsSpan(GoldObjectStateLayout.TypeOffset))));
        var swapped = (byte[])closed.Clone();
        Own(other).CopyTo(Own(swapped));
        StringAssert.Contains(ItemGuard.Check(swapped, tables), "carries the values");

        // An object type that is no item.
        var creature = (byte[])closed.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(creature.AsSpan(GoldObjectStateLayout.TypeOffset), 0x7E5F);
        StringAssert.Contains(ItemGuard.Check(creature, tables), "not an item type");

        // A socket record of no item type.
        var bogus = (byte[])gem.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bogus.AsSpan(Records), 0x7E5F);
        StringAssert.Contains(ItemGuard.Check(bogus, tables), "socket record 0");
    }

    [TestMethod]
    public void NonItemsAreNotChecked()
    {
        Assert.IsNull(ItemGuard.Check(new byte[600], null));
        Assert.IsNull(ItemGuard.Check(default, null));
    }

    [TestMethod]
    public void HeroLevelAboveTheCapIsRefused()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var (live, _) = HeroWriterTests.Upload(HeroWriterTests.Decode(File.ReadAllBytes(save)));
        var hero = live.HeroBody.ToArray();
        var options = new AuthoritativeOptions();
        var level = HeroGuard.Level(hero, full: false);
        Assert.IsTrue(level is > 0 and <= 216, $"level {level}");
        Assert.IsNull(HeroGuard.Check(hero, false, options));
        BinaryPrimitives.WriteUInt16LittleEndian(hero.AsSpan(GoldCreatureStateLayout.PrefixSize + GoldCreatureValuesLayout.CompactWord56Offset), 217);
        StringAssert.Contains(HeroGuard.Check(hero, false, options), "217");
        options.MaxHeroLevel = 0;
        Assert.IsNull(HeroGuard.Check(hero, false, options));
    }
}
