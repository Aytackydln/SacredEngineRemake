using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Wire;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

/// <summary>
/// Hero file writer against real Gold saves. The saves are not part of the repository: set SACRED_HERO_SAVES to a folder
/// with Hero*.pax files (defaults to the developer machine's backup folder); without them the tests are inconclusive.
/// The live state is produced the way the client uploads a Closed Net hero (Sacred.Network's saved-character importers).
/// </summary>
[TestClass]
public class HeroWriterTests
{
    internal const byte Slot = 3;

    internal static IEnumerable<string> Saves()
    {
        var folder = Environment.GetEnvironmentVariable("SACRED_HERO_SAVES") ?? @"B:\sharedstash-backup";
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.pax", SearchOption.AllDirectories)
            .Select(path => (path, size: new FileInfo(path).Length)).DistinctBy(x => x.size).Select(x => x.path).Take(12).ToArray();
    }

    internal sealed record Decoded(byte Version, byte[] Hero, byte[] Objects, byte[] Inventory, byte[]? Shared, List<(uint Resource, byte[] Data)> Records);

    internal static Decoded Decode(byte[] file)
    {
        var inflater = new ZlibInflater();
        byte[] Chunk(uint type)
        {
            var index = HeroFile.FindChunk(file, type, out _, out _);
            if (index < 0) return [];
            var buffer = new byte[1 << 20];
            var length = HeroFile.Decode(file, index, inflater, buffer);
            Assert.IsTrue(length >= 0, $"chunk 0x{type:X} decodes");
            return buffer[..length];
        }
        var objects = Chunk(HeroFile.Objects);
        var records = new List<(uint, byte[])>();
        var position = 4;
        for (var i = 0; i < BinaryPrimitives.ReadUInt32LittleEndian(objects); i++)
        {
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(objects.AsSpan(position)); position += 4;
            if (resource == 0) continue;
            var size = SavedObjects.RecordSize(objects.AsSpan(position), file[3]);
            Assert.IsTrue(size > 0);
            records.Add((resource, objects[position..(position + size)]));
            position += size;
        }
        Assert.AreEqual(objects.Length, position);
        var shared = Chunk(HeroFile.SharedInventory);
        return new(file[3], Chunk(HeroFile.Hero), objects, Chunk(HeroFile.Inventory), shared.Length == 0 ? null : shared, records);
    }

    /// <summary>The client's upload of a saved hero: hero 0x16E, item 0x16E states, both inventories (Sacred.Network importers).</summary>
    internal static (LiveHero Live, Dictionary<uint, byte[]> Bodies) Upload(Decoded d, byte slot = Slot)
    {
        var hero = SacredSavedHero.FromPayload(d.Hero, d.Version).Creature;
        var references = new Dictionary<uint, uint> { [hero.BaseState.ObjectReference] = slot };
        foreach (var (_, data) in d.Records) references[SavedObjects.Reference(data)] = SavedObjects.Reference(data);
        var bodies = new Dictionary<uint, byte[]>();
        foreach (var (_, data) in d.Records)
        {
            if (!SavedObjects.IsItem(data)) continue;
            var item = SacredSavedItem.FromPayload(data, d.Version, data.Length > SacredSavedItemLayout.MinimumSize + data[251] * 130);
            var reference = item.BaseState.ObjectReference;
            var physical = GoldSavedPhysicalStateImporter.ImportParticipantObject(item.PhysicalData, reference, slot);
            var records = new byte[item.Count * 130];
            for (var i = 0; i < item.Count; i++) item.GetRecord(i).CopyTo(records.AsSpan(i * 130));
            bodies[reference] = GoldItemState.Create(physical, records, item.ItemData, slot, item.WeaponExtension).ToPayload();
        }
        var heroPhysical = GoldSavedPhysicalStateImporter.ImportRetainedState(hero.PhysicalData);
        var heroBody = GoldSavedCreatureStateImporter.Import(hero, heroPhysical, references, 0x16E).ToPayload();
        var live = new LiveHero { Slot = slot };
        LiveHero.Store(ref live.Body, ref live.BodyLength, heroBody);
        var inventory = (byte[])d.Inventory.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(inventory, slot);
        LiveHero.Store(ref live.Inventory, ref live.InventoryLength, inventory);
        if (d.Shared is { } shared) LiveHero.Store(ref live.SharedInventory, ref live.SharedInventoryLength, shared);
        live.Objects = (uint reference, out ReadOnlySpan<byte> body) =>
        {
            if (bodies.TryGetValue(reference, out var b)) { body = b; return true; }
            body = default; return false;
        };
        return (live, bodies);
    }

    private static IEnumerable<string> RequireSaves()
    {
        var saves = Saves().ToArray();
        if (saves.Length == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        return saves;
    }

    [TestMethod]
    public void UnchangedHeroRoundTrips()
    {
        foreach (var path in RequireSaves())
        {
            var file = File.ReadAllBytes(path);
            var before = Decode(file);
            var (live, _) = Upload(before);
            var writer = new HeroWriter();
            var output = new byte[HeroWriter.MaxFileBytes];
            var length = writer.Write(file, live, output);
            Assert.IsTrue(length > 0, $"{path}: {writer.Error}");
            var after = Decode(output[..length]);
            CollectionAssert.AreEqual(before.Hero, after.Hero, $"{path}: hero chunk");
            CollectionAssert.AreEqual(before.Inventory, after.Inventory, $"{path}: inventory");
            if (before.Shared is not null) CollectionAssert.AreEqual(before.Shared, after.Shared, $"{path}: shared inventory");
            // Every saved object comes back byte for byte (empty slots are not written again, order may differ).
            var written = after.Records.Select(r => Convert.ToHexString(r.Data)).ToHashSet();
            foreach (var (resource, data) in before.Records)
                Assert.IsTrue(written.Contains(Convert.ToHexString(data)), $"{path}: object {SavedObjects.Reference(data)} (resource {resource}) changed or missing");
            Assert.AreEqual(before.Records.Count, after.Records.Count, $"{path}: object count");
            Assert.AreEqual(0, writer.MissingItems);
            // Sacred.Core's reader accepts the rebuilt container.
            var archive = Sacred.Core.Pax.Hero.SacredHeroPaxArchive.FromPayload(output.AsSpan(0, length));
            for (var i = 0; i < archive.Chunks.Length; i++)
                if (archive.Chunks[i].Type != 0) archive.DecodeChunk(i, 1 << 22);
            // Chunks the server cannot see are copied verbatim.
            foreach (var type in new uint[] { 0xC4, 0xCD })
            {
                var a = HeroFile.FindChunk(file, type, out _, out _); var b = HeroFile.FindChunk(output, type, out _, out _);
                if (a < 0) continue;
                CollectionAssert.AreEqual(file.AsSpan((int)HeroFile.Entry(file, a).Offset, HeroFile.Extent(file, a)).ToArray(),
                    output.AsSpan((int)HeroFile.Entry(output, b).Offset, HeroFile.Extent(output[..length], b)).ToArray(), $"{path}: chunk 0x{type:X}");
            }
        }
    }

    [TestMethod]
    public void SocketsGoldAndCombosAreSaved()
    {
        foreach (var path in RequireSaves())
        {
            var file = File.ReadAllBytes(path);
            var before = Decode(file);
            var (live, bodies) = Upload(before);
            // A socketed item gains a gem record (records are appended after the existing ones, in socket order).
            var socketed = bodies.First(b => BinaryPrimitives.ReadUInt16LittleEndian(b.Value.AsSpan(GoldItemStateLayout.CountOffset)) == 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(b.Value.AsSpan(GoldItemStateLayout.CountOffset)) > 0);
            var body = socketed.Value;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(GoldItemStateLayout.CountOffset));
            var gem = new byte[130]; gem.AsSpan().Fill(0x5A);
            var grown = new byte[body.Length + 130];
            body.AsSpan(0, GoldItemStateLayout.RecordsOffset + count * 130).CopyTo(grown);
            gem.CopyTo(grown, GoldItemStateLayout.RecordsOffset + count * 130);
            body.AsSpan(GoldItemStateLayout.RecordsOffset + count * 130).CopyTo(grown.AsSpan(GoldItemStateLayout.RecordsOffset + (count + 1) * 130));
            BinaryPrimitives.WriteUInt16LittleEndian(grown.AsSpan(GoldItemStateLayout.CountOffset), (ushort)(count + 1));
            bodies[socketed.Key] = grown;
            // Gold and a combo record change in the hero's compact values.
            var hero = live.HeroBody.ToArray();
            var values = GoldCreatureStateLayout.PrefixSize;
            BinaryPrimitives.WriteUInt32LittleEndian(hero.AsSpan(values + GoldCreatureValuesLayout.CompactWord46Offset), 123456);
            var n22 = BinaryPrimitives.ReadUInt16LittleEndian(hero.AsSpan(values + GoldCreatureValuesLayout.CompactCount22Offset));
            var combos = values + GoldCreatureValuesLayout.CompactCount22Offset + 2 + n22 * 22;
            var n38 = BinaryPrimitives.ReadUInt16LittleEndian(hero.AsSpan(combos));
            if (n38 > 0) hero.AsSpan(combos + 2 + 2, 8).Fill(0x11);                 // combo 0: art ids
            LiveHero.Store(ref live.Body, ref live.BodyLength, hero);

            var writer = new HeroWriter();
            var output = new byte[HeroWriter.MaxFileBytes];
            var length = writer.Write(file, live, output);
            Assert.IsTrue(length > 0, $"{path}: {writer.Error}");
            var after = Decode(output[..length]);
            var saved = SacredSavedHero.FromPayload(after.Hero, after.Version).Creature.Values;
            Assert.AreEqual(123456u, saved.Gold, path);
            if (n38 > 0) CollectionAssert.AreEqual(hero.AsSpan(combos + 2, 38).ToArray(), saved.GetRecord38(0).ToArray(), $"{path}: combo");
            Assert.AreEqual(n38, saved.Count38);
            var item = after.Records.Single(r => SavedObjects.Reference(r.Data) == socketed.Key).Data;
            var parsed = SacredSavedItem.FromPayload(item, after.Version, item.Length > SacredSavedItemLayout.MinimumSize + item[251] * 130);
            Assert.AreEqual(count + 1, parsed.Count, $"{path}: socket count");
            CollectionAssert.AreEqual(gem, parsed.GetRecord(count).ToArray(), $"{path}: gem record");
            CollectionAssert.AreEqual(body.AsSpan(GoldItemStateLayout.RecordsOffset + count * 130, 130).ToArray(), parsed.ItemData.ToArray(), $"{path}: item data");
            Assert.AreEqual(SavedObjects.RecordSize(item, after.Version), item.Length);
            // The block round-trips through the lobby format.
            var block = new byte[HeroFile.BlockHeaderBytes + length + 1024];
            var blockLength = HeroFile.WriteBlock(output.AsSpan(0, length), new ZlibDeflater(), block);
            var back = new byte[HeroWriter.MaxFileBytes];
            Assert.AreEqual(length, HeroFile.ReadBlock(block.AsSpan(0, blockLength), new ZlibInflater(), back));
            CollectionAssert.AreEqual(output[..length], back[..length]);
        }
    }

    [TestMethod]
    public void TooManySocketRecordsAreRefused()
    {
        var body = new byte[GoldItemStateLayout.MinimumSize + 256 * 130];
        BinaryPrimitives.WriteUInt32LittleEndian(body, GoldObjectStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldItemStateLayout.MarkerOffset), SavedObjects.ItemMarker);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(GoldItemStateLayout.CountOffset), 256);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldItemStateLayout.RecordsOffset + 257 * 130 + 4), SavedObjects.ItemMarker);
        Assert.IsTrue(SavedObjects.ReadLiveItem(body, out var records, out _));
        Assert.AreEqual(256, records);
        Assert.AreEqual(-1, SavedObjects.WriteItem(body, 20, new byte[400], new byte[64 * 1024]));
    }
}
