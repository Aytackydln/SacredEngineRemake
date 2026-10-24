using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class QuestDeltaChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Quest deltas: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-quest-deltas.json")));
        foreach (var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = SacredItemCategory.Container });
            Seed(world, fixture.RootElement.GetProperty("initial"));
            var quests = world.WorldBanks.Quests;
            quests.JournalRange.Configure(sample.GetProperty("active_before").GetUInt32());
            var opcode = sample.GetProperty("opcode").GetUInt16(); var index = sample.GetProperty("index").GetUInt32();
            if (sample.GetProperty("present").GetBoolean())
                world.Apply(Actor(sample.GetProperty("flags").GetUInt32(), opcode == 0x18d, opcode == 0x170));
            var bytes = Bytes(sample, "payload_hex"); var entry = Event(opcode, bytes); Array.Fill(bytes, (byte)0xa5);
            world.Apply(entry);
            Check(quests.Entries.Count == sample.GetProperty("entries_count").GetUInt32(), "native entry vector growth");
            Check(quests.Database.Count == sample.GetProperty("database_count").GetUInt32(), "native database growth");
            Check(quests.Headers.Count == sample.GetProperty("headers_count").GetUInt32(), "native header growth");
            Check(quests.Journal.Count == sample.GetProperty("journal_count").GetUInt32(), "native journal growth");
            Check(world.WorldBanks.IndexedRecords.Table.Count == sample.GetProperty("indexed_count").GetUInt32(), "native 188-byte vector growth");
            Check(quests.JournalRange.End == sample.GetProperty("active_after").GetUInt32(), "native active journal end");
            Check(quests.PendingCount == 0, "complete baseline consumes all progress work");
            if (sample.TryGetProperty("headers", out var headers))
                for (var i = 0; i < headers.GetArrayLength(); i++) Match(quests.Headers.Find((uint)i)!, headers[i]);
            if (sample.TryGetProperty("database_record", out var database) && database.ValueKind != JsonValueKind.Null)
                Match(quests.Database.Find(index)!, database);
            if (opcode == 0x1bb) Match(quests.Headers.Find(index)!, sample.GetProperty("header_record"));
            if (opcode == 0x1ba)
            {
                var result = Bytes(sample, "record_hex"); var state = quests.Entries.Find(index)!;
                var retained = state.ToPayload()!;
                foreach (var offset in GoldQuestLayout.EntryCallbackOffsets)
                {
                    // Function lookup is stubbed in the fixture; validate its exact inputs separately.
                    retained.AsSpan(offset, 4).CopyTo(result.AsSpan(offset, 4));
                    Check(sample.GetProperty("resolutions").EnumerateArray().Any(v => v.GetUInt32() == BinaryPrimitives.ReadUInt32LittleEndian(retained.AsSpan(offset))), "native callback hash argument");
                }
                Check(retained.SequenceEqual(result), "all native quest-entry bytes outside callback remapping");
                Check(state.RuntimeWord == BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(288)), "entry runtime word");
                Check(quests.Entries.UnknownSlotCount == quests.Entries.Count - 3 + (index < 2 ? 1u : 0u), "growth holes stay unknown");
            }
            if (opcode == 0x1bd) Check(quests.Journal.Find(index)!.Data.SequenceEqual(Bytes(sample, "record_hex")), "native journal gap and wire mapping");
            if (opcode == 0x15d)
            {
                var actual = world.WorldBanks.IndexedRecords.Table.Find(index)!; var expected = Bytes(sample, "record_hex");
                for (var i = 0; i < expected.Length; i++)
                    if (actual.TryGetByte(i, out var value)) Check(value == expected[i], "native 188-byte field assignment and preserved padding");
                Check(actual.IsComplete == (index < 2), "new padding is unknown; baseline padding stays known");
            }
            if (world.Objects.TryGetValue(500, out var actor))
            {
                Check(actor.BaseState.Flags == sample.GetProperty("actor_flags").GetUInt32(), "native receiver flags");
                if (opcode == 0x1b9)
                {
                    Check(actor.Creature!.Word200 == sample.GetProperty("creature_flags").GetUInt32(), "native quest inserted flag");
                    Check(actor.Physical!.QuestNumber == sample.GetProperty("quest_number").GetInt16(), "native quest-number tag");
                }
            }
            if (opcode is 0x170 or 0x18d)
            {
                var calls = sample.GetProperty("calls"); var dispatches = calls.GetArrayLength();
                Check(world.ObjectPresentation.Pending.Count == (dispatches == 0 ? 0 : 1), "native virtual-call guard and ordered work");
                if (opcode == 0x170 && dispatches != 0)
                {
                    var animation = (GoldObjectAnimationEvent)world.ObjectPresentation.Pending[0].Event;
                    Check(animation.Animation == calls[0][1].GetUInt32() && animation.FirstArgumentBits == calls[0][2].GetUInt32() &&
                        animation.SecondArgumentBits == calls[0][3].GetUInt32(), "exact CreateAnimation float bit arguments");
                    Check(world.Objects[500].Physical!.Word64 == 0, "virtual failure does not install a guessed successful motion");
                }
            }
            world.Clear();
            Check(quests.Database.Count == 0 && quests.Entries.Count == 0 && quests.Headers.Count == 0 && quests.Journal.Count == 0 &&
                world.WorldBanks.IndexedRecords.Table.Count == 0 && world.ObjectPresentation.Pending.Count == 0, "disconnect clears all ten receives");
        }
        Console.WriteLine($"PASS: {checks} native quest/object delta comparisons.");
        return checks;

        void Match(GoldWorldBankRecord actual, JsonElement expected)
        {
            Check(actual.Prefix.SequenceEqual(Bytes(expected, "prefix_hex")), "native fixed prefix");
            var vectors = expected.GetProperty("vectors_hex"); Check(actual.Vectors.Count == vectors.GetArrayLength(), "native vector count");
            for (var i = 0; i < actual.Vectors.Count; i++) Check(actual.Vectors[i].Data.SequenceEqual(Convert.FromHexString(vectors[i].GetString()!)), "native vector values");
            var children = expected.GetProperty("children"); Check(actual.ChildRecords.Count == children.GetArrayLength(), "native nested child count");
            for (var i = 0; i < actual.ChildRecords.Count; i++)
            {
                Check(actual.ChildRecords[i].Data.SequenceEqual(Bytes(children[i], "fields_hex")), "native child fields");
                Check(actual.ChildRecords[i].Vectors[0].Data.SequenceEqual(Bytes(children[i], "words_hex")), "native child vector");
            }
        }
    }
}
