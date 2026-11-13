using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupCreatureSerializationChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root, "native-startup-creature-serialization");
        using var installation = Read(root, "native-startup-equipment-installation");
        using var selection = Read(root, "native-equipment-slot-selection");
        var data = EquipmentGenerationFixture.ReadStartup(root);
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(selection.RootElement, "override_code_hex"));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var memory = Bytes(row, "actor_hex");
            var actor = Actor(memory);
            var references = new GoldObjectReferencePool();
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(27u).Concat(BitConverter.GetBytes(28u)).ToArray()));
            var factory = new WorldEquipmentInitialFactory(references, new(10), new(), 1);
            var installed = actor with { EquipmentReferences = new uint[19] };
            foreach (var number in new[] { 0, 2 })
            {
                var itemRow = installation.RootElement.GetProperty("cases")[number];
                var type = itemRow.GetProperty("type").GetUInt32();
                var item = factory.Create(data.Templates.GetItem(type), data.Templates.GetInstance(type), 1, networking: true);
                WorldScriptEquipmentGeneration.Apply(item, itemRow.GetProperty("generation_level").GetUInt16(), 1, data, new(itemRow.GetProperty("generation_seed").GetUInt32()), SacredX87Precision.Single);
                installed = WorldCreatureInitialEquipmentInstallation.Apply(installed, item, type, data, slots, factory.Items, networking: true);
            }
            var actual = WorldCreatureInitialSerialization.Write(actor, data.Templates.GetItem(actor.Base.ObjectType), factory.Items, data.Templates.GetItem).ToPayload();
            var expected = Bytes(row, "payload_hex");
            Check(actual.AsSpan(0, 109).SequenceEqual(expected.AsSpan(0, 109)) && actual.AsSpan(121).SequenceEqual(expected.AsSpan(121)),
                "complete placed creature stream outside the ignored bone cache; differences " + string.Join(",", Enumerable.Range(0, Math.Min(actual.Length, expected.Length)).Where(index => actual[index] != expected[index]).Select(index => index.ToString())));
            if (row.GetProperty("fill").GetByte() == 0) Check(actual.AsSpan().SequenceEqual(expected), "canonical bytes match the complete actual zero-stack caller");
            Check(actual.AsSpan(109, 12).SequenceEqual(new byte[12]), "ignored creature cache canonicalized without forcing flags");
            var message = GoldObjectStateCodec.Create(0x16f, actor.Base.Owner, actual);
            Check(message.Payload.AsSpan(0, 24).SequenceEqual(Bytes(row, "publication_prefix_hex")), "actual CreateNPC outer publication arguments");
            Check(actor.EquipmentReferences[12] == 28 && actor.EquipmentReferences[13] == 27, "serialization preserves installed graph");
            foreach (var publication in row.GetProperty("publications").EnumerateArray().Skip(1))
            {
                var expectedItem = Bytes(publication, "payload_hex");
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(expectedItem.AsSpan(4));
                var item = factory.Items[reference];
                var actualItem = WorldEquipmentInitialSerialization.Write(item, data.Templates.GetItem(item.Physical.Base.ObjectType)).ToPayload();
                Check(actualItem.AsSpan(0, 109).SequenceEqual(expectedItem.AsSpan(0, 109)) && actualItem.AsSpan(121).SequenceEqual(expectedItem.AsSpan(121)), "actual installed equipment stream outside the discarded unplaced cache");
                var itemMessage = GoldObjectStateCodec.Create(0x16e, actor.Base.Owner, actualItem);
                Check(itemMessage.Payload.AsSpan(0, 8).SequenceEqual(Bytes(publication, "prefix_hex")), "actual equipment outer publication arguments");
            }
            Check(Bytes(row, "final_actor_hex").AsSpan().SequenceEqual(memory), "default inventory/optional-effect gates and cleanup do not change the actor");
            var prepared = WorldCreatureInitialPublication.Prepare(actor, GoldCreatureState.FromPayload(actual), factory.Items, data.Templates.GetItem);
            var expectedPublications = row.GetProperty("publications").EnumerateArray().ToArray();
            Check(prepared.Count == expectedPublications.Length, "native complete publication request count");
            for (var index = 0; index < prepared.Count; index++)
            {
                var publication = prepared[index];
                var prefix = publication.Opcode == 0x16f ? 24 : 8;
                var expectedBody = Bytes(expectedPublications[index], "payload_hex");
                Check(publication.Opcode == expectedPublications[index].GetProperty("opcode").GetUInt16() &&
                    publication.Payload.AsSpan(0, prefix).SequenceEqual(Bytes(expectedPublications[index], "prefix_hex")) &&
                    publication.Payload.AsSpan(prefix, 109).SequenceEqual(expectedBody.AsSpan(0, 109)) &&
                    publication.Payload.AsSpan(prefix + 121).SequenceEqual(expectedBody.AsSpan(121)), "native publication order and complete canonical message bytes");
            }
            Check(factory.Items.Values.All(item => item.PacketReady && item.SerializedState is not null), "complete initial graph retains both prepared item streams");
        }
        foreach (var group in fixture.RootElement.GetProperty("position_consumers").EnumerateArray())
        {
            var expected = Bytes(group, "after_without_cache_hex");
            var vectors = group.GetProperty("vectors_hex").EnumerateArray().ToArray();
            var index = 0;
            foreach (var row in group.GetProperty("results").EnumerateArray())
            {
                var after = Bytes(row, "after_hex");
                Check(after.AsSpan(0x98, 12).SequenceEqual(Convert.FromHexString(vectors[index++].GetString()!)), "placed reader retains actual cache bits");
                after.AsSpan(0x98, 12).Clear();
                Check(after.AsSpan().SequenceEqual(expected) && row.GetProperty("position_hex").GetString() == group.GetProperty("position_hex").GetString(), "native position and all other physical state are independent of the cache");
                Check(row.GetProperty("cache_read_count").GetInt32() == 0, "actual creature position path never reads the cache");
            }
        }
        Check(fixture.RootElement.GetProperty("noncreature_control_reads").GetArrayLength() > 0, "non-creature control verifies native cache-read instrumentation");
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Startup creature serialization: " + why); checks++; }
    }

    public static byte[] WireExample(string root)
    {
        using var fixture = Read(root, "native-startup-creature-serialization");
        var row = fixture.RootElement.GetProperty("cases")[0];
        return Bytes(row, "publication_prefix_hex").Concat(Bytes(row, "payload_hex")).ToArray();
    }

    public static byte[] EquipmentWireExample(string root, int index)
    {
        using var fixture = Read(root, "native-startup-creature-serialization");
        var publication = fixture.RootElement.GetProperty("cases")[0].GetProperty("publications")[index + 1];
        return Bytes(publication, "prefix_hex").Concat(Bytes(publication, "payload_hex")).ToArray();
    }

    private static WorldCreatureInitialActor Actor(byte[] b) => new(new(Word(b, 12), Word(b, 16), Word(b, 20), Short(b, 24),
        Int(b, 28), Int(b, 32), b[36], Word(b, 40), Word(b, 52), b[56], b[57], Word(b, 60)),
        new(b.AsSpan(0x3b0, 238).ToArray(), b.AsSpan(0x4d0, 24).ToArray(), b[0x4ce], b[0x4cf]),
        Word(b, 0x1f0), Word(b, 0x1f4), Short(b, 0x25f), Short(b, 0x574), 0, 0, [])
    {
        Direction = new(BitConverter.UInt32BitsToSingle(Word(b, 0x70)), BitConverter.UInt32BitsToSingle(Word(b, 0x74)), BitConverter.UInt32BitsToSingle(Word(b, 0x78))),
        ScriptInitialPosition = new(Int(b, 0x2bd), Int(b, 0x2c1), b[0x2c5], Short(b, 0x2b9)),
        ScriptGroup = Word(b, 0x249), RuntimeFlags = Word(b, 0x200), Flags2B7 = Short(b, 0x2b7),
        EquipmentReferences = Enumerable.Range(0, 19).Select(index => Word(b, 0x1a4 + index * 4)).ToArray()
    };
    private static JsonDocument Read(string root, string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
