using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>Quest state in server-written heroes: chunk CD (explored map) and the inventory quest pairs (0x145).</summary>
[TestClass]
public class QuestStateTests
{
    private static IEnumerable<string> RequireSaves()
    {
        var saves = HeroWriterTests.Saves().ToArray();
        if (saves.Length == 0) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        return saves;
    }

    internal static byte[] DecodedChunk(ReadOnlySpan<byte> file, uint decodedType)
    {
        var index = HeroFile.FindChunk(file, decodedType, out _, out _);
        if (index < 0) return [];
        var buffer = new byte[1 << 21];
        var length = HeroFile.Decode(file, index, new ZlibInflater(), buffer);
        Assert.IsTrue(length >= 0, $"chunk 0x{decodedType:X} decodes");
        return buffer[..length];
    }

    [TestMethod]
    public void ExploredMapLayoutMatchesRealSaves()
    {
        foreach (var path in RequireSaves())
        {
            var file = File.ReadAllBytes(path);
            var map = DecodedChunk(file, 0xCC);
            if (map.Length == 0) continue;
            var count = ExploredMap.Validate(map, out var present, out _);
            Assert.IsTrue(count > 0, $"{path}: CC layout");
            Assert.AreEqual(ExploredMap.HeaderBytes + count * ExploredMap.EntryBytes + present * ExploredMap.MaskBytes, map.Length, path);
            // Sacred.Core's archive reader decodes the same bytes.
            var archive = Sacred.Core.Pax.Hero.SacredHeroPaxArchive.FromPayload(file);
            var index = Array.FindIndex(archive.Chunks.ToArray(), c => c.Type == 0xCD);
            CollectionAssert.AreEqual(map, archive.DecodeChunk(index, 1 << 22).ToArray(), path);
        }
    }

    [TestMethod]
    public void UnchangedHeroKeepsAnIdenticalDecodedExploredMap()
    {
        foreach (var path in RequireSaves())
        {
            var file = File.ReadAllBytes(path);
            var before = HeroWriterTests.Decode(file);
            var (live, _) = HeroWriterTests.Upload(before);
            var writer = new HeroWriter();
            var output = new byte[HeroWriter.MaxFileBytes];
            var length = writer.Write(file, live, output);
            Assert.IsTrue(length > 0, $"{path}: {writer.Error}");
            var map = DecodedChunk(file, 0xCC);
            var saved = DecodedChunk(output.AsSpan(0, length), 0xCC);
            CollectionAssert.AreEqual(map, saved, $"{path}: decoded CD");
            Assert.AreEqual(ExploredMap.Validate(map, out _, out var explored), ExploredMap.Validate(saved, out _, out var savedExplored), path);
            Assert.AreEqual(explored, savedExplored, path);
            CollectionAssert.AreEqual(before.Inventory, HeroWriterTests.Decode(output[..length]).Inventory, $"{path}: quest pairs unchanged");
        }
    }

    [TestMethod]
    public void QuestPairMessageChangesExactlyTheQuestRecords()
    {
        foreach (var path in RequireSaves())
        {
            var file = File.ReadAllBytes(path);
            var before = HeroWriterTests.Decode(file);
            var (live, _) = HeroWriterTests.Upload(before);
            var saved = SacredSavedInventory.FromPayload(before.Inventory);
            // A key no saved pair uses (0x5D8280 refuses a known key and any vector holding a zero word04).
            var blocked = Enumerable.Range(0, saved.QuestCount).Any(i => SacredSavedInventoryQuestRecords.Read(saved, i).Word04 == 0);
            uint key = 0x1450;
            while (Enumerable.Range(0, saved.QuestCount).Any(i => SacredSavedInventoryQuestRecords.Read(saved, i).KeyWord == key)) key++;
            const uint word04 = 0x0BADF00D;

            var result = QuestRecords.Apply(live, key, word04);
            if (blocked) { Assert.AreEqual(QuestRecords.Result.Refused, result, path); continue; }
            Assert.AreEqual(QuestRecords.Result.Appended, result, path);
            Assert.AreEqual(QuestRecords.Result.Refused, QuestRecords.Apply(live, key, 1), $"{path}: same key again");

            var writer = new HeroWriter();
            var output = new byte[HeroWriter.MaxFileBytes];
            var length = writer.Write(file, live, output);
            Assert.IsTrue(length > 0, $"{path}: {writer.Error}");
            var after = HeroWriterTests.Decode(output[..length]);
            // Exactly the repo's own append: count + 1, the pair at the end of the vector, the scrolls moved up by 8 bytes.
            var expected = SacredSavedInventoryQuestRecords.Append(saved, key, word04).ToPayload();
            CollectionAssert.AreEqual(expected, after.Inventory, $"{path}: inventory with the new pair");
            var reread = SacredSavedInventory.FromPayload(after.Inventory);
            Assert.AreEqual(saved.QuestCount + 1, reread.QuestCount, path);
            var pair = SacredSavedInventoryQuestRecords.Read(reread, saved.QuestCount);
            Assert.AreEqual(key, pair.KeyWord); Assert.AreEqual(word04, pair.Word04);
            for (var i = 0; i < saved.QuestCount; i++)
                Assert.AreEqual(SacredSavedInventoryQuestRecords.Read(saved, i), SacredSavedInventoryQuestRecords.Read(reread, i), $"{path}: pair {i}");
            // Byte diff: everything before the insertion point is identical, the rest is shifted, nothing else changed.
            var at = SacredSavedInventoryLayout.QuestRecordsOffset + saved.QuestCount * 8;
            for (var i = 0; i < at; i++)
                if (i < SacredSavedInventoryLayout.QuestCountOffset || i >= SacredSavedInventoryLayout.QuestCountOffset + 4)
                    Assert.AreEqual(before.Inventory[i], after.Inventory[i], $"{path}: byte {i}");
            CollectionAssert.AreEqual(before.Inventory[at..], after.Inventory[(at + 8)..], $"{path}: scrolls");
            // The other chunks are untouched by the pair.
            CollectionAssert.AreEqual(before.Hero, after.Hero, $"{path}: hero");
            CollectionAssert.AreEqual(DecodedChunk(file, 0xCC), DecodedChunk(output.AsSpan(0, length), 0xCC), $"{path}: decoded CD");
        }
    }

    [TestMethod]
    public void QuestPairRuleFollowsTheClient()
    {
        var stream = new byte[SacredSavedInventoryLayout.MinimumSize + 64];
        var length = SacredSavedInventoryLayout.MinimumSize;
        BinaryPrimitives.WriteUInt32LittleEndian(stream.AsSpan(SacredSavedInventoryLayout.QuestRecordsOffset), 2);      // 2 scrolls
        length += 8;
        Assert.AreEqual(QuestRecords.Result.Appended, QuestRecords.Append(stream, length, 7, 9)); length += 8;
        Assert.AreEqual(QuestRecords.Result.Refused, QuestRecords.Append(stream, length, 7, 10), "known key");
        Assert.AreEqual(QuestRecords.Result.NoRoom, QuestRecords.Append(stream.AsSpan(0, length), length, 1, 1));
        Assert.AreEqual(QuestRecords.Result.Appended, QuestRecords.Append(stream, length, 8, 0)); length += 8;
        Assert.AreEqual(QuestRecords.Result.Refused, QuestRecords.Append(stream, length, 9, 1), "a zero word04 blocks the vector");
        var inventory = SacredSavedInventory.FromPayload(stream.AsSpan(0, length));
        Assert.AreEqual(2, inventory.QuestCount);
        Assert.AreEqual(7u, SacredSavedInventoryQuestRecords.Read(inventory, 0).KeyWord);
        Assert.AreEqual(8u, SacredSavedInventoryQuestRecords.Read(inventory, 1).KeyWord);
        Assert.AreEqual(QuestRecords.Result.Malformed, QuestRecords.Append(stream, 100, 1, 1));
    }
}
