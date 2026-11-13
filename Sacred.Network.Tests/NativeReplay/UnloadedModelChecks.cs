using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Core.Pax.Hero.Objects;
using System.Runtime.InteropServices;

internal static class UnloadedModelChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-unloaded-models.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var after = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var item = new WorldEquipmentObject(new(U(before, 12), U(before, 16), U(before, 20),
                BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan(24)), I(before, 28), I(before, 32), before[36],
                U(before, 40), U(before, 52), before[56], before[57], U(before, 60)), row.GetProperty("physical").GetBoolean(), true)
            {
                PhysicalFlags = U(before, 0xb0), Direction = Vector(before, 0x70),
                Vector7C = Vector(before, 0x7c), Vector88 = Vector(before, 0x88)
            };
            item.SetLink(U(before, 44), (item.Base.Flags & 0x400) != 0);
            if (row.GetProperty("operation").GetString() == "reset")
            {
                // Direct reset starts at a physical method, independent of the
                // lookup/RTTI controls used by the attachment cases.
                item = new(item.Base, true, true) { PhysicalFlags = item.PhysicalFlags, Direction = item.Direction,
                    Vector7C = item.Vector7C, Vector88 = item.Vector88 };
                item.SetLink(U(before, 44), (item.Base.Flags & 0x400) != 0);
                WorldUnloadedModelEffects.Reset(item);
            }
            else
            {
                var slot = row.GetProperty("slot").GetUInt16();
                var references = new uint[19]; references[slot] = 500;
                var actor = new WorldCreatureEquipmentState(new(100, 295, row.GetProperty("actor_flags").GetUInt32(),
                    0, 0, 0, 0, 0, 0, 0, 0, 0), false, references);
                var result = WorldUnloadedModelEffects.Attach(actor, slot,
                    reference => reference == 500 && row.GetProperty("exists").GetBoolean() ? item : null);
                if (result != (ushort)row.GetProperty("result").GetUInt32()) throw new Exception($"Native unloaded attachment slot differs: {row}");
                checks++;
            }
            var actual = (byte[])before.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(20), item.Base.Flags);
            Write(actual, 0x70, item.Direction); Write(actual, 0x7c, item.Vector7C); Write(actual, 0x88, item.Vector88);
            if (!actual.SequenceEqual(after)) throw new Exception($"Native unloaded model mutation differs: {row}");
            if (item.PhysicalFlags != U(before, 0xb0) || item.Link != U(before, 44)) throw new Exception("Unloaded model changed physical flags/parent link.");
            checks += actual.Length + 2;
        }
        foreach (var row in fixture.RootElement.GetProperty("bone_state").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetProperty("state_hex").GetString()!);
            if (row.GetProperty("result").GetInt32() != 6 || bytes.Any(value => value != row.GetProperty("fill").GetByte()))
                throw new Exception("Original zero-handle bone state is not an untouched error result.");
            checks += bytes.Length + 1;
        }
        using var constructor = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-constructor.json")));
        var source = constructor.RootElement.GetProperty("cases").EnumerateArray().First(row => row.GetProperty("type").GetUInt32() == 295 && row.GetProperty("fill").GetByte() == 0);
        var nativeActor = Convert.FromHexString(source.GetProperty("actor_hex").GetString()!);
        var physical = GoldPhysicalConstructionWriter.Write(new(500, 295, 2, 0, 0, 0, 0, 0, 0, 0, 1, 0), default);
        var values = GoldCreatureValuesWriter.Write(0x16f, Convert.FromHexString(source.GetProperty("values_hex").GetString()!),
            Convert.FromHexString(source.GetProperty("health_hex").GetString()!));
        var generated = GoldCreatureConstructionWriter.Write(physical, values, U(nativeActor, 0x1f0), U(nativeActor, 0x1f4),
            BinaryPrimitives.ReadUInt16LittleEndian(nativeActor.AsSpan(0x25f)), BinaryPrimitives.ReadUInt16LittleEndian(nativeActor.AsSpan(0x574))).ToPayload();
        byte[]? imported = null;
        foreach (var row in fixture.RootElement.GetProperty("unplaced_streams").EnumerateArray())
        {
            var expected = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            if (!generated.AsSpan(0, 109).SequenceEqual(expected.AsSpan(0, 109)) || !generated.AsSpan(121).SequenceEqual(expected.AsSpan(121)))
                throw new Exception("Native unloaded unplaced creature stream differs outside its uninitialized bone-vector bytes.");
            if (row.GetProperty("fill").GetByte() == 0 && !generated.SequenceEqual(expected))
                throw new Exception("Zero-canonicalized construction differs from the original zero-stack writer.");
            var after = Convert.FromHexString(row.GetProperty("physical_after_hex").GetString()!);
            if (imported is not null && !after.SequenceEqual(imported)) throw new Exception("Unplaced receiver retained uninitialized bone-vector bytes.");
            imported = after;
            if (U(after, 0x98) != 0 || U(after, 0x9c) != 0 || U(after, 0xa0) != 0 || (U(after, 0xb0) & 4) == 0)
                throw new Exception("Native unplaced physical receive must store zero +98 and set physical bit 4.");
            checks += generated.Length + after.Length + 4;
        }
        foreach (var row in fixture.RootElement.GetProperty("activation").EnumerateArray())
        {
            var actor = new WorldCreatureInitialActor(new(0, 295, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new(Convert.FromHexString(source.GetProperty("values_hex").GetString()!), Convert.FromHexString(source.GetProperty("health_hex").GetString()!), 6, 0),
                7, 0, 0, 0, 0, 0, new byte[100]);
            var queue = new WorldObjectActivationQueue(row.GetProperty("count").GetUInt32());
            var clock = row.GetProperty("clock").GetDouble();
            var record = Convert.FromHexString(row.GetProperty("record_hex").GetString()!);
            var saved = MemoryMarshal.Read<SacredSavedObjectBaseLayout>(Convert.FromHexString(row.GetProperty("saved_base_hex").GetString()!));
            if (!queue.ActivateInitial(actor, clock) || !actor.Node.Active || actor.Node.Flags != row.GetProperty("flags").GetByte() ||
                queue.ActivationCount != row.GetProperty("count_after").GetUInt32() || queue.Nodes.Count != 1 || !ReferenceEquals(queue.Nodes[0], actor.Node))
                throw new Exception($"Native initial node activation differs: {row}");
            if (BitConverter.DoubleToUInt64Bits(actor.Node.StartedAt) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) ||
                BitConverter.DoubleToUInt64Bits(actor.Node.NextDue) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)))
                throw new Exception($"Native initial node activation time differs: {row}; {actor.Node.StartedAt:R}, {actor.Node.NextDue:R}");
            if (queue.ActivateInitial(actor, clock) || queue.Nodes.Count != 1 || queue.ActivationCount != row.GetProperty("count_after").GetUInt32() || actor.Base.Flags != 2)
                throw new Exception("Duplicate activation changed count/queue or loaded a model.");
            if (!saved.NodeActive || saved.NodeQueueKind != actor.Node.QueueKind || saved.NodePeriodMilliseconds != actor.Node.PeriodMilliseconds)
                throw new Exception("Original saved node header differs from mapped fields.");
            checks += 13;
        }
        foreach (var row in fixture.RootElement.GetProperty("equipment_events").EnumerateArray())
        {
            WorldCreatureConstructionEvents.EquipmentChanged(row.GetProperty("ki_mode").GetUInt16(), row.GetProperty("slot").GetUInt16());
            if (row.GetProperty("mutated").GetBoolean()) throw new Exception("Original initial equipment notification unexpectedly mutates actor state.");
            checks++;
        }
        var loaded = new WorldEquipmentObject(new(500, 1729, GoldPhysicalStateLayout.ModelLoadedFlag | 0x200,
            0, 0, 0, 0, 0, 0, 0, 0, 0), true, true) { Vector7C = new(1, 2, 3), Vector88 = new(4, 5, 6) };
        try { WorldUnloadedModelEffects.Reset(loaded); throw new Exception("Loaded model accepted by unloaded reset."); }
        catch (NotSupportedException) { checks++; }
        if (loaded.Vector7C != new GoldPhysicalVector(1, 2, 3) || loaded.Vector88 != new GoldPhysicalVector(4, 5, 6))
            throw new Exception("Loaded model reset failed after mutation.");
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-unloaded-models.json")));
        var row = fixture.RootElement.GetProperty("unplaced_streams").EnumerateArray().First(value => value.GetProperty("fill").GetByte() == 0);
        var body = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
        var packet = new byte[24 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, 500); BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 295);
        body.CopyTo(packet, 24);
        return packet;
    }
    private static uint U(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int I(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static GoldPhysicalVector Vector(byte[] bytes, int at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)), BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8)));
    private static void Write(byte[] bytes, int at, GoldPhysicalVector value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), value.Z);
    }
}
