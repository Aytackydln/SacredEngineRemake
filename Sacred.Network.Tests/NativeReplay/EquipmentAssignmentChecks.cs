using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;

internal static class EquipmentAssignmentChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        void Reject(Action action)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Malformed assignment was accepted.");
        }
        using var prefixes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-creature-state.json")));
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-equipment-assignment.json")));
        var original = Convert.FromHexString(prefixes.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
        // Original item fixture supplies the valid item marker/record layers.
        using var itemFixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-item-state.json")));
        var itemOriginal = Convert.FromHexString(itemFixtures.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
        foreach (var sample in fixtures.RootElement.GetProperty("samples").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.ConfigureCreatureClasses(new Dictionary<uint,SacredCreatureClass> { [456] = sample.GetProperty("horse").GetBoolean() ? SacredCreatureClass.Horse : 0 });
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            var entry = (GoldEquipmentAssignmentEvent)GoldGameEventDecoder.Decode(new(0x11E,1,1,0,bytes));
            Check(GoldEquipmentAssignmentCodec.Create(1,1,entry.CreatureReference,entry.Slot,entry.ItemReference).Payload.SequenceEqual(bytes),"wire parity");
            var actorBytes = original.ToArray();
            var flags = sample.GetProperty("actor_flags_before").GetUInt32();
            var basis = GoldObjectBaseState.FromPayload(actorBytes) with { ObjectReference = 77, ObjectType = 456, Flags = flags };
            basis.ToPayload().CopyTo(actorBytes,0);
            // Base writer clears 0x10000; fixture flags deliberately have that bit clear.
            if (entry.Slot < 19) BinaryPrimitives.WriteUInt32LittleEndian(actorBytes.AsSpan(GoldCreatureStateLayout.EquipmentOffset+(int)entry.Slot*4),123);
            if (sample.GetProperty("actor_present").GetBoolean()) world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E,1,actorBytes)));
            var itemBytes = itemOriginal.ToArray();
            var itemBasis = GoldObjectBaseState.FromPayload(itemBytes) with { ObjectReference=900, Flags=0x100002, Sector=321, X=456, Y=789, Surface=3 };
            itemBasis.ToPayload().CopyTo(itemBytes,0);
            if (sample.GetProperty("item_known").GetBoolean()) world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E,1,itemBytes)));
            var beforeRevision = world.Revision;
            world.Apply(entry);
            var changed = sample.GetProperty("changed").GetBoolean();
            Check(world.Revision==beforeRevision+(changed?1:0),"native guards decide mutation");
            if (world.Objects.TryGetValue(77,out var actor))
            {
                var expected=actorBytes.ToArray();
                if (entry.Slot<19) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(GoldCreatureStateLayout.EquipmentOffset+(int)entry.Slot*4),sample.GetProperty("after_reference").GetUInt32());
                Check(actor.Creature!.ToPayload().SequenceEqual(expected),"all unrelated creature bytes retained");
                Check(actor.BaseState.Flags==sample.GetProperty("actor_flags_after").GetUInt32(),"native special slot flag parity");
            }
            if (world.Objects.TryGetValue(900,out var item))
            {
                var expected=sample.GetProperty("item_relocated").GetBoolean()
                    ? itemBasis with { Flags=2, Sector=0, X=0, Y=0, Surface=0 } : itemBasis;
                Check(item.BaseState==expected,"known assignment item location/flags parity");
                Check(item.Item!.ToPayload().SequenceEqual(itemBytes),"subtype payload retained");
            }
        }
        var valid=GoldEquipmentAssignmentCodec.Create(1,1,77,13,900);
        for(var length=0;length<12;length++) Reject(()=>GoldGameEventDecoder.Decode(valid with {Payload=valid.Payload[..length]}));
        Reject(()=>GoldGameEventDecoder.Decode(valid with {Payload=valid.Payload.Concat(new byte[1]).ToArray()}));
        var delivered=0; Reject(()=>GoldGameDispatcher.Dispatch([valid,valid with {Payload=[]}],_=>delivered++));
        Check(delivered==0,"malformed assignment batch is atomic");
        Console.WriteLine($"PASS: {checks} equipment assignment checks against 224 original instruction cases.");
        return checks;
    }
}
