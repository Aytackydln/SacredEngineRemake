using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.GameplayHost;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupGroundEquipmentChecks
{
    public static int Run(string root)
    {
        using var source = Read(root);
        var checks = 0;
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var prepared = Prepare(source.RootElement, row);
            var actual = prepared.Result;
            var item = actual.Item;
            var expected = Bytes(row.GetProperty("publications")[0], "payload_hex");
            expected.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
            Check(item.SerializedState!.ToPayload().AsSpan().SequenceEqual(expected),
                $"complete type {row.GetProperty("type")} / profile {row.GetProperty("profile")} native stream after proven cache canonicalization");
            Check(actual.Publication.Payload.AsSpan(0,8).SequenceEqual(Bytes(row.GetProperty("publications")[0], "prefix_hex")) &&
                actual.Publication.Payload.AsSpan(8).SequenceEqual(expected) && actual.PacketReady && prepared.World.Created.Count == 1,
                "complete private publication prefix/state and readiness of this object only");
            var native = Bytes(row, "item_hex");
            var basis = new GoldObjectBaseState(Word(native,12),Word(native,16),Word(native,20),
                BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(24)),(int)Word(native,28),(int)Word(native,32),native[36],
                Word(native,40),Word(native,52),native[56],native[57],Word(native,60));
            Check(item.Physical.Base == basis, "all native runtime identity/placement/flag fields, including graphic registration");
            Check(item.Values.AsSpan().SequenceEqual(native.AsSpan(0x118,130)) && item.Values.AsSpan().SequenceEqual(Bytes(row,"original_values_hex")) && !item.Generated,
                "all authored values retained; ordinary construction does not generate equipment");
            var callbacks = Bytes(row, "callbacks_hex").Chunk(GoldObjectScriptCallback.Size).Select(bytes => GoldObjectScriptCallback.FromPayload(bytes)).ToArray();
            Check(actual.Callbacks.Select(value => (value.ObjectReference,value.Name)).SequenceEqual(callbacks.Select(value => (value.ObjectReference,value.Name))),
                "native ordered Use/Take callbacks, including empty suffix");
            Check(prepared.References.Available == 1 && prepared.Counter.Value == 38 && prepared.Factory.Items.Count == 1 &&
                prepared.References.TryAllocate(out var next) && next == 55, "one shared reservation and one counter increment");
            var record = Bytes(row, "record_hex");
            Check(!item.Node.Active && item.Node.Flags == native[8] && item.Node.PeriodMilliseconds == Word(native,4) &&
                prepared.Activation.ActivationCount == 1 && prepared.Activation.Nodes.Count == 0 && prepared.Activation.Records.Single().Flags == record[4],
                "actual activation, deactivation and retained scheduler tombstone");
            Check(BitConverter.DoubleToUInt64Bits(item.Node.StartedAt) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) &&
                BitConverter.DoubleToUInt64Bits(item.Node.NextDue) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)), "native scheduler clocks");
            Check(row.GetProperty("rng_calls").GetArrayLength() == 0 && row.GetProperty("rng_after").GetUInt32() == row.GetProperty("seed").GetUInt32(), "native complete caller consumes no RNG");
            var extension = item.SerializedState.WeaponExtension;
            Check(item.SerializedState.HasWeaponExtension == (row.GetProperty("type").GetUInt32() != 1200) &&
                (extension.IsEmpty || extension[4..].IndexOfAnyExcept((byte)0) < 0), "weapon extension marker/four defaults; Shield ordinary trailer");
        }
        using var positions = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-ground-equipment-position.json")));
        var rows = positions.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        foreach (var group in rows.GroupBy(row => (row.GetProperty("type").GetUInt32(),row.GetProperty("profile").GetInt32(),row.GetProperty("fill").GetByte(),row.GetProperty("root_found").GetBoolean())))
        {
            var native = group.Single(row => row.GetProperty("name").GetString() == "native");
            var zero = group.Single(row => row.GetProperty("name").GetString() == "zero");
            var valid = group.Single(row => row.GetProperty("name").GetString() == "valid_nearby");
            Check(native.GetProperty("position_hex").GetString() == zero.GetProperty("position_hex").GetString() &&
                native.GetProperty("state_sha256").GetString() == zero.GetProperty("state_sha256").GetString(), "actual position consumer rejects incidental cache identically to zero");
            Check(valid.GetProperty("position_hex").GetString() != zero.GetProperty("position_hex").GetString(), "accepted nearby-cache positive control affects actual position");
        }
        foreach (var row in rows)
            Check(row.GetProperty("cache_hex").GetString() == row.GetProperty("retained_hex").GetString() &&
                row.GetProperty("consumed").GetInt32() == (row.GetProperty("type").GetUInt32() == 1200 ? 265 : 285), "complete class-specific reader consumes all bytes and retains the supplied cache before update");
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new Exception("Ground equipment: " + reason); checks++; }
    }

    public static byte[] WireExample(string root, uint type)
    {
        using var source = Read(root);
        var row = source.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("type").GetUInt32() == type && value.GetProperty("profile").GetInt32() == 0);
        return Prepare(source.RootElement,row).Result.Publication.Payload;
    }

    private static Preparation Prepare(JsonElement source, JsonElement row)
    {
        var type = row.GetProperty("type").GetUInt32();
        var descriptor = new ItemsPakEntry(new((ushort)type,0,128,0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row,"descriptor_hex")),"original");
        var weapon = new byte[256 + 258]; Bytes(source,"weapon_header_hex").CopyTo(weapon,0);
        BinaryPrimitives.WriteUInt32LittleEndian(weapon.AsSpan(4),1);
        Bytes(row,"header_hex").CopyTo(weapon,256); Bytes(row,"original_values_hex").CopyTo(weapon,384);
        var templates = SacredObjectTemplateArchive.FromPayload(new Dictionary<uint,ItemsPakEntry> { [type] = descriptor },weapon);
        var command = new SacredScriptCommand(row.GetProperty("source_offset").GetInt32(),Bytes(row,"command_hex"));
        var creation = SacredScriptObjectCreationReader.Read(command,_ => throw new Exception("Unexpected symbolic lookup"));
        var tile = creation.TilePosition!.Value;
        var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24),1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c),SacredWorldCoordinates.TileCenterToWorld(tile.X / 64 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40),SacredWorldCoordinates.TileCenterToWorld(tile.Y / 64 * 64) - 25);
        var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(54u).Concat(BitConverter.GetBytes(55u)).ToArray()));
        var counter = new WorldObjectAllocationCounter(37); var activation = new WorldObjectActivationQueue();
        var factory = new WorldEquipmentInitialFactory(references,counter,activation,1);
        var world = new WorldScriptItemWorld(templates,factory,new(1,1,false,0,()=>1000),SacredSectorIndex.FromPayload(keyx).CreateGrid(),_=>{});
        return new(world.Create(command,creation,descriptor),world,factory,references,counter,activation);
    }
    private sealed record Preparation(WorldScriptCreatedItem Result, WorldScriptItemWorld World, WorldEquipmentInitialFactory Factory,
        GoldObjectReferencePool References, WorldObjectAllocationCounter Counter, WorldObjectActivationQueue Activation);
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-ground-equipment.json")));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
