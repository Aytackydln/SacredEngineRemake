using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Lobby;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

public partial class AuthoritativeTests
{
    /// <summary>A 0x145 quest pair from the player reaches the saved inventory; it is forwarded and applied to the other in-game player too.</summary>
    [TestMethod]
    public void QuestPairFromThePlayerIsSaved()
    {
        var save = HeroWriterTests.Saves().FirstOrDefault();
        if (save is null) Assert.Inconclusive("no hero saves (set SACRED_HERO_SAVES)");
        var file = File.ReadAllBytes(save);
        var decoded = HeroWriterTests.Decode(file);
        var savedInventory = SacredSavedInventory.FromPayload(decoded.Inventory);
        if (Enumerable.Range(0, savedInventory.QuestCount).Any(i => SacredSavedInventoryQuestRecords.Read(savedInventory, i).Word04 == 0))
            Assert.Inconclusive("the first save's quest pairs refuse every append");
        uint key = 0x2468;
        while (Enumerable.Range(0, savedInventory.QuestCount).Any(i => SacredSavedInventoryQuestRecords.Read(savedInventory, i).KeyWord == key)) key++;

        var baseline = new byte[file.Length + 4096];
        var baselineLength = HeroFile.WriteBlock(file, new ZlibDeflater(), baseline);
        using var fake = new LobbyTests.FakeLobby();
        using var server = new Server(c => { c.Auth.SaveDebounceMilliseconds = 60_000; c.Auth.SaveIntervalSeconds = 0; }, fake);   // only the leave save
        var player = RawClient.Admit(server.Port, "Hero", 500, block: 2);
        fake.WaitFor(LobbyProtocol.UserJoinedServer);
        var push = new byte[18 + baselineLength];
        BinaryPrimitives.WriteInt32LittleEndian(push, 500);
        BinaryPrimitives.WriteUInt16LittleEndian(push.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(6), baselineLength);
        BinaryPrimitives.WriteInt32LittleEndian(push.AsSpan(14), baselineLength);
        baseline.AsSpan(0, baselineLength).CopyTo(push.AsSpan(18));
        fake.SendLobby(LobbyProtocol.PublicData, push);
        player.WaitFor(0x132);
        player.Send(0x016, default); player.Send(0x018, Word(0x1964)); player.WaitFor(0x018);
        using var other = RawClient.Admit(server.Port, "Other", 0);
        other.Send(0x015, default); other.Send(0x017, default); other.WaitFor(0x017);

        var (live, bodies) = HeroWriterTests.Upload(decoded, player.Slot);
        byte[] State(uint reference, byte[] body)
        {
            var payload = new byte[8 + body.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
            body.CopyTo(payload, 8);
            return payload;
        }
        foreach (var (reference, body) in bodies) player.Send(0x16E, State(reference, body));
        player.Send(0x16E, State(player.Slot, live.HeroBody.ToArray()));
        player.Send(0x122, [.. Word(player.Slot), .. live.InventoryStream.ToArray()]);
        if (!live.SharedInventoryStream.IsEmpty) player.Send(0x122, [.. Word(17), .. live.SharedInventoryStream.ToArray()]);
        var otherInventory = (byte[])decoded.Inventory.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(otherInventory, other.Slot);
        other.Send(0x122, [.. Word(other.Slot), .. otherInventory]);
        player.Send(QuestRecords.Opcode, [.. Word(key), .. Word(0x77)]);
        var forwarded = other.WaitFor(QuestRecords.Opcode);
        CollectionAssert.AreEqual((byte[])[.. Word(key), .. Word(0x77)], forwarded.Payload);
        player.Send(QuestRecords.Opcode, [.. Word(key), .. Word(0x78)]);            // same key: refused like the client does
        Thread.Sleep(100);
        Assert.AreEqual(2, server.Host.QuestRecordsApplied, "sender and recipient");
        player.Dispose();

        var upload = fake.WaitFor(LobbyProtocol.ServerDataUpload).Body;
        var saved = new byte[HeroWriter.MaxFileBytes];
        var savedLength = HeroFile.ReadBlock(upload[18..], new ZlibInflater(), saved);
        Assert.IsTrue(savedLength > 0);
        var after = HeroWriterTests.Decode(saved[..savedLength]);
        CollectionAssert.AreEqual(SacredSavedInventoryQuestRecords.Append(savedInventory, key, 0x77).ToPayload(), after.Inventory);
        CollectionAssert.AreEqual(QuestStateTests.DecodedChunk(file, 0xCC), QuestStateTests.DecodedChunk(saved.AsSpan(0, savedLength), 0xCC), "CD as delivered");
    }
}
