using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ObjectLifecycleChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; }
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-lifecycle.json")));
        foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            var flags = sample.GetProperty("flags").GetUInt32();
            if (sample.GetProperty("present").GetBoolean())
                world.Apply(Object(new(37, 100, flags, 1, 123, 456, 0, 0, 42, 9, 7, 31)));
            var message = new GoldMessage(sample.GetProperty("opcode").GetUInt16(), 0, 1, 0,
                Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!));
            var entry = GoldGameEventDecoder.Decode(message);
            world.Apply(entry);
            if (entry is GoldObjectOwnerEvent owner)
            {
                Check(owner.ObjectReference == BinaryPrimitives.ReadUInt32LittleEndian(message.Payload) &&
                    owner.OwnerWord == BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(4)), "owner words retained without removal mask");
                if (world.Objects.TryGetValue(37, out var obj))
                    Check(obj.BaseState.Owner == sample.GetProperty("owner_after").GetByte(), "native active/missing/inactive owner write");
            }
            else
            {
                var removal = (GoldObjectRemovalEvent)entry;
                Check(removal.ObjectReference == sample.GetProperty("effective_reference").GetUInt32(), "native removal high-bit mask");
                if (world.Objects.TryGetValue(37, out var obj))
                {
                    var called = sample.GetProperty("calls").EnumerateArray().Any(call => call[0].GetString() is "ordinary" or "immediate");
                    Check(obj.BaseState.Flags == (called ? flags | 0x10 : flags), $"native inactive bit: opcode {message.Opcode:X}, ref {removal.ObjectReference}, before {flags:X}, after {obj.BaseState.Flags:X}, called {called}");
                    Check(obj.RemovalOpcode == (called ? message.Opcode : (ushort?)null), "normal/immediate notification retained only when applied");
                }
            }
            Check(world.Objects.Count == (sample.GetProperty("present").GetBoolean() ? 1 : 0), "unknown references do not fabricate replicas");
        }
        foreach (var opcode in new ushort[] { 0x166, 0x167, 0x171 })
        foreach (var length in new[] { 0, 1, 3, 5, 7, 9, 32 })
        {
            try { GoldGameEventDecoder.Decode(new(opcode, 0, 1, 0, new byte[length])); throw new Exception("Invalid lifecycle size accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        var fixture = new GoldWorldReplica(); fixture.Apply(Object(new(37, 100, 0, 1, 123, 456, 0, 0, 42, 9, 7, 31)));
        var original = fixture.Objects[37];
        try
        {
            GoldGameDispatcher.Dispatch([Message(0x171, 37, 2), new(0x166, 0, 1, 0, [1])], fixture.Apply);
            throw new Exception("Late malformed lifecycle batch accepted.");
        }
        catch (InvalidDataException) { checks++; }
        Check(fixture.Objects[37] == original, "late malformed batch cannot change an earlier owner");
        fixture.Apply(GoldGameEventDecoder.Decode(Message(0x166, 37)));
        var revision = fixture.Revision;
        fixture.Apply(GoldGameEventDecoder.Decode(Message(0x167, 37)));
        fixture.Apply(GoldGameEventDecoder.Decode(Message(0x171, 37, 3)));
        Check(fixture.Revision == revision && fixture.Objects[37].BaseState.Owner == 7, "duplicate deletion and late owner cannot revive a retired object");
        fixture.Apply(Object(original.BaseState));
        Check(fixture.Objects[37].RemovalOpcode is null && (fixture.Objects[37].BaseState.Flags & 0x10) == 0, "full state reuses a retired reference with a fresh lifecycle");
        fixture.Clear();
        Check(fixture.Objects.Count == 0, "disconnect clears inactive records as well as active replicas");
        Console.WriteLine($"PASS: {checks} lifecycle native receive, malformed batch and reference-reuse checks.");
        return checks;
    }

    public static int RunCharacter(GoldParticipantCharacterUpload upload)
    {
        var world = new GoldWorldReplica(); world.SeedCharacter(upload);
        var hero = world.Objects[upload.PlayerSlot];
        world.Apply(GoldGameEventDecoder.Decode(Message(0x167, upload.PlayerSlot)));
        if (world.Objects[upload.PlayerSlot] != hero) throw new Exception("Native participant local-hero removal protection lost.");
        var source = (GoldObjectEvent)GoldGameEventDecoder.Decode(upload.CreateMessages().Single(message => message.Opcode == 0x169));
        var remote = upload.PlayerSlot == 3 ? 4u : 3u;
        var state = source.ObjectState.ToArray();
        (source.BaseState with { ObjectReference = remote, Owner = (byte)remote }).ToPayload().CopyTo(state, 0);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x169, upload.PlayerSlot, state)));
        world.Apply(new GoldMovementEvent(new(0x163, 0, 0, 0, []), remote, 1, 2, 3, 4, 5, []));
        world.Apply(GoldGameEventDecoder.Decode(Message(0x166, remote)));
        var retired = world.Objects[remote];
        world.Apply(new GoldMovementEndEvent(new(0x162, 0, 0, 0, []), remote, 1, 2, 3, 4, 5, []));
        world.Apply(new GoldMovementEvent(new(0x163, 0, 0, 0, []), remote, 1, 2, 3, 4, 5, []));
        if (world.Objects[remote] != retired || retired.Movement is not null) throw new Exception("Late movement changed an inactive actor.");
        world.Apply(Object(source.BaseState with { ObjectReference = remote }));
        if (world.Objects[remote].Creature is not null || world.Objects[remote].Movement is not null)
            throw new Exception("Reference reuse inherited a deleted instance's typed/movement state.");
        var sectors = new byte[62];
        BinaryPrimitives.WriteUInt16LittleEndian(sectors.AsSpan(4), source.BaseState.Sector);
        world.Apply(GoldGameEventDecoder.Decode(new(0x148, 0, 1, 0, sectors)));
        world.PredictLocalPosition(new(source.BaseState.X, source.BaseState.Y, source.BaseState.Surface, source.BaseState.Sector), 0);
        var otherOwner = upload.PlayerSlot == 3 ? 4u : 3u;
        world.Apply(GoldGameEventDecoder.Decode(Message(0x171, upload.PlayerSlot, otherOwner)));
        try { world.PredictLocalPosition(new(0, 0, 0, source.BaseState.Sector), 0); throw new Exception("Prediction accepted a foreign-owned hero."); }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS: 4 saved-character lifecycle checks; protected local hero, inactive late movement, fresh reference state and ownership guard.");
        return 4;
    }

    private static GoldObjectEvent Object(GoldObjectBaseState state) => (GoldObjectEvent)GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E, 1, state.ToPayload()));
    private static GoldMessage Message(ushort opcode, uint reference, uint? owner = null)
    {
        var payload = new byte[owner is null ? 4 : 8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        if (owner is { } value) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), value);
        return new(opcode, 0, 1, 0, payload);
    }
}
