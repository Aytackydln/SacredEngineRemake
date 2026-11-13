using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupHorseChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Read("native-startup-horse");
        using var equipment = Read("native-startup-horse-equipment");
        using var constructor = Read("native-horse-constructor");
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var levels = Read("native-creature-level-selection");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { constructor, basis, initialization, finalization, precision })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var balance = Bytes(constructor.RootElement, "balance_hex");
        var results = WorldCreatureScalarResultAssets.Read(image, balance);
        var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
        Bytes(fixture.RootElement, "template_hex").CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
        var creatures = CreaturePakArchive.Parse(archive);
        var type = creatures.Entries[1].ItemType;
        var descriptor = new ItemsPakEntry(new((ushort)type, 0, 128, 0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(fixture.RootElement, "descriptor_hex")), "");
        var itemData = Bytes(equipment.RootElement, "shared_hex");
        var equipmentDescriptor = new ItemsPakEntry(new(3201, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(equipment.RootElement, "descriptor_hex")), "");
        var weaponHeader = new byte[256 + 258];
        itemData.CopyTo(weaponHeader, 256); BinaryPrimitives.WriteUInt32LittleEndian(weaponHeader.AsSpan(4), 1); BinaryPrimitives.WriteUInt32LittleEndian(weaponHeader, 0x084e5057);
        var objects = SacredObjectTemplateArchive.FromPayload(new Dictionary<uint, ItemsPakEntry> { [type] = descriptor, [3201] = equipmentDescriptor }, weaponHeader);
        var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c), SacredWorldCoordinates.TileCenterToWorld(52 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40), SacredWorldCoordinates.TileCenterToWorld(39 * 64) - 25);
        var assets = new WorldScriptCreatureAssets(objects, creatures, results, SacredCreatureConstructionDefinitions.Read(image),
            CreatureLevelBalanceBinReader.Read(balance), CreatureBalanceBinReader.Read(balance),
            SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement, "cap_override_code_hex")), SacredSectorIndex.FromPayload(keyx))
            { Equipment = EquipmentGenerationFixture.ReadStartup(root) };
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter = new WorldObjectAllocationCounter(48);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var world = new WorldScriptCreatureWorld(assets, references, counter, new(1, 1, false, 0, () => 1000), random, _ => { });
            var context = new WorldScriptExecutionContext(new WorldScriptDefinitions(), [], ReadOnlyMemory<byte>.Empty, true, random.Next) { CreatureWorld = world };
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(fixture.RootElement, "command_hex"));
            Check(context.StartupCompleted && !context.IsInvalidated && world.Pending is null, "complete actual authored command: " + context.Failure);
            var prepared = world.Prepared.Single(); var actor = prepared.Actor!;
            var native = Bytes(row, "actor_hex");
            Block(actor.Component.Values, native.AsSpan(0x3b0, 238), "complete final values");
            Block(actor.Component.Health, native.AsSpan(0x4d0, 24), "complete final health");
            Check(actor.Component.ResultValid == native[0x4cf], "network result validity");
            Check(random.State == row.GetProperty("rng_after").GetUInt32() && counter.Value == 49 && references.Available == 0, "RNG/reference/counter lifetime");
            Check(!actor.Node.Active && prepared.Equipment.Count == 0 && actor.EquipmentReferences.All(reference => reference == 0), "unmounted dormant empty equipment horse");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x570)) == 0, "native caller preserves absent rider link");
            var publication = prepared.Publications.Single(); var expected = row.GetProperty("publications")[0];
            Block(publication.Payload.AsSpan(0, 24), Bytes(expected, "prefix_hex"), "publication arguments");
            var body = publication.Payload.AsSpan(24); var nativeBody = Bytes(expected, "payload_hex");
            Check(body.Length == nativeBody.Length, "complete stream length");
            Block(body[..109], nativeBody.AsSpan(0,109), "complete stream prefix");
            Block(body[121..], nativeBody.AsSpan(121), "complete stream tail, including role/link/group fields");
            Check(body.Slice(109,12).SequenceEqual(new byte[12]), "canonical discarded creature position cache");
            Check(world.Resources.ObjectBindings.TryGetValue(17465, out var bound) && bound == 65, "native named binding");
            if (row.GetProperty("seed").GetUInt32() == 2004 && row.GetProperty("fill").GetInt32() == 0)
            {
                references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(66u)));
                var equipContext = new WorldScriptExecutionContext(new WorldScriptDefinitions(), [], ReadOnlyMemory<byte>.Empty, true, random.Next) { CreatureWorld = world };
                new WorldScriptExecutor(equipContext).ExecuteStartup(Bytes(equipment.RootElement, "command_hex"));
                var item = world.EquipmentFactory.Items[66]; var itemMemory = Bytes(equipment.RootElement, "item_hex");
                Block(item.Values, itemMemory.AsSpan(0x118,130), "EquipItem preserves native original values");
                Check(counter.Value == 50 && references.Available == 0 && random.State == equipment.RootElement.GetProperty("rng_after").GetUInt32(), "EquipItem counter/reference/RNG");
                Check(item.Physical.AttachmentParent == 0 && actor.EquipmentReferences.All(value => value == 0), "native nonzero Horse slot rejection");
                var itemPublication = world.EquipmentCommands.Publications.Single(); var expectedItem = equipment.RootElement.GetProperty("publications")[0];
                Block(itemPublication.Payload.AsSpan(0,8), Bytes(expectedItem,"prefix_hex"), "EquipItem publication arguments");
                var expectedBody = Bytes(expectedItem,"payload_hex");
                Block(itemPublication.Payload.AsSpan(8,109), expectedBody.AsSpan(0,109), "uninstalled item publication prefix");
                Block(itemPublication.Payload.AsSpan(8+121), expectedBody.AsSpan(121), "uninstalled item publication tail");
                Check(itemPublication.Payload.AsSpan(8+109,12).SequenceEqual(new byte[12]), "unplaced flag-2 item discards the canonical bone cache");
                Block(actor.Component.Values, Bytes(equipment.RootElement,"after_hex").AsSpan(0x3b0,238), "EquipItem retains actor results");
            }
        }
        var ownerOperands = SacredScriptOperandReader.Read(new(0, Bytes(fixture.RootElement,"owner_command_hex")));
        Check(ownerOperands.Count == 2 && ownerOperands[1].Kind == SacredScriptArgumentKind.CreatureOwner && ownerOperands[1].Text == "res:17466", "next SetNPCState owner operand representation");
        var ownerDefinition = Bytes(fixture.RootElement,"owner_definition_hex");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(ownerDefinition.AsSpan(12)) == 17466 &&
            System.Text.Encoding.Latin1.GetString(ownerDefinition.AsSpan(112,10)) == "res:17466\0", "native owner definition offsets; application remains separate");
        return checks;
        void Check(bool pass, string field) { if (!pass) throw new Exception("Startup Horse: " + field); checks++; }
        void Block(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string field)
        {
            if (!actual.SequenceEqual(expected)) throw new Exception("Startup Horse: " + field + ": " + Convert.ToHexString(actual) + " != " + Convert.ToHexString(expected));
            checks += actual.Length;
        }
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-horse.json")));
        var row = fixture.RootElement.GetProperty("cases")[0].GetProperty("publications")[0];
        return Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex")).ToArray();
    }
    public static byte[] EquipmentWireExample(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-horse-equipment.json")));
        var row = fixture.RootElement.GetProperty("publications")[0];
        return Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex")).ToArray();
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}

