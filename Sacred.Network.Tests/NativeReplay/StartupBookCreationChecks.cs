using System.Collections.Frozen;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameRes;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupBookCreationChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root);
        var source = fixture.RootElement;
        var command = new SacredScriptCommand(0xA93D, Bytes(source,"command_hex"));
        var creation = SacredScriptObjectCreationReader.Read(command, _ => throw new Exception("Unexpected symbolic lookup."));
        var descriptor = new ItemsPakEntry(new((ushort)creation.TypeId,0,128,0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(source,"descriptor_hex")), "native");
        var weapon = new byte[256+258];
        Bytes(source,"weapon_header_hex").CopyTo(weapon,0); BinaryPrimitives.WriteUInt32LittleEndian(weapon.AsSpan(4),1);
        Bytes(source,"header_hex").CopyTo(weapon,256); Bytes(source,"original_values_hex").CopyTo(weapon,384);
        var templates = SacredObjectTemplateArchive.FromPayload(new Dictionary<uint,ItemsPakEntry> { [creation.TypeId]=descriptor },weapon);
        var keyx = new byte[256+768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256+0x24),1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x3c),SacredWorldCoordinates.TileCenterToWorld(50*64)-25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x40),SacredWorldCoordinates.TileCenterToWorld(44*64)-25);
        var grid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
        var checks = 0;
        void Check(bool pass,string message) { if (!pass) throw new Exception(message); checks++; }
        foreach (var row in source.GetProperty("cases").EnumerateArray())
        {
            var references = new GoldObjectReferencePool(); var grant = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(grant,54); BinaryPrimitives.WriteUInt32LittleEndian(grant.AsSpan(4),55);
            references.Apply(GoldReferenceBatch.FromPayload(grant));
            var counter = new WorldObjectAllocationCounter(37); var activation = new WorldObjectActivationQueue();
            var factory = new WorldEquipmentInitialFactory(references,counter,activation,1);
            var descriptorBytes=Bytes(source,"descriptor_hex");
            BinaryPrimitives.WriteSingleLittleEndian(descriptorBytes.AsSpan(87),row.GetProperty("descriptor_angle").GetSingle());
            var sampleDescriptor=new ItemsPakEntry(descriptor.EntryInfo,MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(descriptorBytes),"native");
            var full = row.GetProperty("mode").GetString()=="creation";
            WorldEquipmentInitialItem item;
            if (full)
            {
                var sampleCommand = new SacredScriptCommand(row.GetProperty("source_offset").GetInt32(),Bytes(row,"command_hex"));
                var sampleCreation = SacredScriptObjectCreationReader.Read(sampleCommand,_=>throw new Exception("Unexpected symbolic lookup."));
                var tile = sampleCreation.TilePosition!.Value;
                BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x3c),SacredWorldCoordinates.TileCenterToWorld(tile.X/64*64)-25);
                BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x40),SacredWorldCoordinates.TileCenterToWorld(tile.Y/64*64)-25);
                var sampleGrid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
                var world = new WorldScriptItemWorld(templates,factory,new(1,1,false,0,()=>1000),sampleGrid,_=>{});
                var strings=new Dictionary<uint,string>();
                if (row.GetProperty("resource_text").GetString() is { } text && sampleCreation.Name is { } name && name.StartsWith("Res:",StringComparison.OrdinalIgnoreCase))
                    strings[SacredResourceHash.ComputeDefaultCLocale(System.Text.Encoding.Latin1.GetBytes(name[4..]))]=text;
                var resources=new WorldScriptItemResources(new GoldScriptResourceReplica(),new GameResStore(strings.ToFrozenDictionary()));
                Check(row.GetProperty("binding_value").GetUInt32()==(row.GetProperty("bindings").GetArrayLength()>0 ? 54u : row.GetProperty("binding_before").GetUInt32()),
                    "actual native insertion slot overwrites the previous object binding");
                var expectedBindings=new Dictionary<uint,uint>();
                if (row.GetProperty("binding_before").GetUInt32() is >0 and var previous) { resources.Replica.BindObject(8300,previous); expectedBindings[8300]=previous; }
                foreach(var key in row.GetProperty("bindings").EnumerateArray()) expectedBindings[key.GetUInt32()]=54;
                var result = world.Create(sampleCommand,sampleCreation,sampleDescriptor,resources); item = result.Item;
                var expected = Bytes(row.GetProperty("publications")[0],"payload_hex"); expected.AsSpan(109,12).Clear();
                var actual=item.SerializedState!.ToPayload();
                Check(actual.AsSpan().SequenceEqual(expected), $"Complete Book command source={row.GetProperty("source_offset")} text={row.GetProperty("resource_text")} angle={row.GetProperty("descriptor_angle")} fill={row.GetProperty("fill")}; differences: {string.Join(",",Enumerable.Range(0,Math.Min(actual.Length,expected.Length)).Where(i=>actual[i]!=expected[i]).Take(12).Select(i=>$"{i:X}:{actual[i]:X2}/{expected[i]:X2}"))}.");
                Check(result.Publication.Payload.AsSpan(8).SequenceEqual(expected) && result.PacketReady && world.Created.Count==1,
                    "Complete publication remains privately retained on the constructed item.");
                Check(resources.Replica.ObjectBindings.Count==expectedBindings.Count && expectedBindings.All(pair=>resources.Replica.ObjectBindings.TryGetValue(pair.Key,out var value) && value==pair.Value),"native unconditional resource/object map binding");
                var names=Bytes(row,"name_records_hex").Chunk(72).Select(bytes=>GoldScriptNameRecord.FromPayload(bytes)).ToArray();
                Check(resources.Replica.Names.Count==names.Length && names.Select((name,index)=>resources.Replica.Names[index].Name==name.Name &&
                    resources.Replica.Names[index].ResourceId==name.ResourceId && resources.Replica.Names[index].TriggerReference==name.TriggerReference && !resources.Replica.Names[index].HasCompleteBytes).All(v=>v),
                    $"native local aliases source={row.GetProperty("source_offset")} text={row.GetProperty("resource_text")}: actual={string.Join("/",resources.Replica.Names.Select(n=>n.Name))}; expected={string.Join("/",names.Select(n=>n.Name))}");
                var callback = GoldObjectScriptCallback.FromPayload(Bytes(row,"callbacks_hex"));
                Check(result.Callbacks.Single().Name==callback.Name && result.Callbacks.Single().ObjectReference==callback.ObjectReference,
                    "Original callback registration preserves object identity and Take prefix without invoking it.");
                Check(!item.Generated && item.Values.AsSpan().SequenceEqual(Bytes(source,"original_values_hex")),
                    "Native command copies authored Book values without equipment generation or a placeholder recalculation.");
            }
            else item = factory.Create(sampleDescriptor,Bytes(source,"original_values_hex"),1,true);
            var native = Bytes(row,"item_hex");
            var expectedBase = new GoldObjectBaseState(Word(native,12),Word(native,16),Word(native,20),
                BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(24)),(int)Word(native,28),(int)Word(native,32),native[36],
                Word(native,40),Word(native,52),native[56],native[57],Word(native,60));
            Check(item.Physical.Base==expectedBase,"Original constructor/caller identity, flags, logical placement, counter and owner.");
            Check(item.Physical.Direction==Vector(native,0x70) && item.Physical.PhysicalFlags==Word(native,0xb0),
                $"Original Book physical heading and flags: mode={row.GetProperty("mode")} fill={row.GetProperty("fill")} offset={row.GetProperty("source_offset")}; actual={item.Physical.Direction}/{item.Physical.PhysicalFlags:X}, native={Vector(native,0x70)}/{Word(native,0xb0):X}.");
            Check(item.Values.AsSpan().SequenceEqual(native.AsSpan(0x118,130)),"Every original item instance byte is retained.");
            Check(counter.Value==38 && references.Available==1 && references.TryAllocate(out var next) && next==55 && factory.Items.Count==1,
                "One shared reservation, one counter increment and one registry entry.");
            var record = Bytes(row,"record_hex");
            Check(item.Node.Flags==native[8] && item.Node.PeriodMilliseconds==Word(native,4) && activation.ActivationCount==1 &&
                activation.Records.Single().Flags==record[4] && activation.Nodes.Count==(full?0:1),"Native activation, direct deactivation and scheduler tombstone.");
            Check(BitConverter.DoubleToUInt64Bits(item.Node.StartedAt)==BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) &&
                BitConverter.DoubleToUInt64Bits(item.Node.NextDue)==BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)),"Exact original activation clock and period.");
            Check(row.GetProperty("rng_calls").GetArrayLength()==0 && row.GetProperty("rng_after").GetUInt32()==row.GetProperty("seed").GetUInt32(),
                "Actual constructor and caller consume no shared RNG.");
        }
        using var positions = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-book-position.json")));
        var rows = positions.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        foreach (var group in rows.GroupBy(row=>(row.GetProperty("source_offset").GetInt32(),row.GetProperty("root_found").GetBoolean())))
        {
            var native=group.Single(row=>row.GetProperty("name").GetString()=="native");
            var zero=group.Single(row=>row.GetProperty("name").GetString()=="zero");
            var valid=group.Single(row=>row.GetProperty("name").GetString()=="valid_nearby");
            Check(native.GetProperty("position_hex").GetString()==zero.GetProperty("position_hex").GetString() &&
                native.GetProperty("state_sha256").GetString()==zero.GetProperty("state_sha256").GetString(),"Actual reader/update rejects the native incidental cache and recomputes the same position as zero.");
            Check(valid.GetProperty("position_hex").GetString()!=zero.GetProperty("position_hex").GetString(),"Valid nearby cache positive control changes position; non-creature caches are not globally ignored.");
        }
        foreach (var row in rows)
            Check(row.GetProperty("cache_hex").GetString()==row.GetProperty("retained_hex").GetString() && row.GetProperty("consumed").GetInt32()==265,
                "Complete native item reader retains every supplied cache variant before update.");
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var source = Read(root);
        var row = source.RootElement.GetProperty("cases").EnumerateArray().First(value=>value.GetProperty("mode").GetString()=="creation");
        return GoldObjectStateCodec.Create(0x16e,1,Bytes(row.GetProperty("publications")[0],"payload_hex")).Payload;
    }
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-books.json")));
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static GoldPhysicalVector Vector(byte[] bytes,int offset)=>new(BitConverter.UInt32BitsToSingle(Word(bytes,offset)),
        BitConverter.UInt32BitsToSingle(Word(bytes,offset+4)),BitConverter.UInt32BitsToSingle(Word(bytes,offset+8)));
}
