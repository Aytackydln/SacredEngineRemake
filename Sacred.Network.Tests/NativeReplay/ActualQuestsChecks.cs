using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ActualQuestsChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Actual quests: " + why); checks++; }
        GoldGameEvent Event(ushort op, byte[] data) => GoldGameEventDecoder.Decode(new(op, 0, 1, 2, data));
        byte[] Bytes(JsonElement row, string field) => Convert.FromHexString(row.GetProperty(field).GetString()!);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-actual-quests.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var op = row.GetProperty("opcode").GetUInt16();
            if (row.GetProperty("ignored").GetBoolean())
            {
                Check(op == 0x1ca && row.GetProperty("mode").GetString() == "server", "full upload is unsupported on server");
                Check(Bytes(row, "initial_hex").SequenceEqual(Bytes(row, "result_hex")), "unsupported server upload leaves native table untouched");
                Check(row.GetProperty("relays").GetArrayLength() == 0, "unsupported upload has no relay");
                continue; // The managed world is a client projection, not a server receiver.
            }
            var world = new GoldWorldReplica(); var table = world.WorldBanks.ActualQuests;
            world.Apply(Event(0x1ca, Bytes(row, "initial_hex")));
            var wire = Bytes(row, "payload_hex"); var retained = wire.ToArray();
            var entry = Event(op, wire); Array.Fill(wire, (byte)0xee);
            var revision = world.Revision; world.Apply(entry);
            Check(table.TryGetPayload(out var actual), "complete table stays complete");
            Check(actual.SequenceEqual(Bytes(row, "result_hex")), "native loop/prefix byte result");
            Check(entry.Message.Payload.SequenceEqual(retained), "owned payload including reset suffix");
            Check(table.KnownByteCount == 128 && world.Revision == revision+1, "known byte count and revision");
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no object or callback effects");
            for (var slot = 0; slot < 16; slot++)
            {
                Check(table.GetQuestWord(slot) == BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(slot*8)), "first raw word");
                Check(table.GetCompanionWord(slot) == BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(slot*8+4)), "companion raw word");
            }
            actual[0] ^= 255;
            Check(table.TryGetPayload(out var copy) && !copy.SequenceEqual(actual), "export owns its storage");
        }
        foreach (var row in fixture.RootElement.GetProperty("banks").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var table = world.WorldBanks.ActualQuests;
            world.Apply(Event(0x1ca, Bytes(row, "initial_hex")));
            var prefix = Bytes(row, "payload_hex"); var bank = new byte[40+prefix.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(bank.AsSpan(32), (uint)prefix.Length);
            prefix.CopyTo(bank, 40); world.Apply(Event(0x1b4, bank));
            Check(table.TryGetPayload(out var actual) && actual.SequenceEqual(Bytes(row, "result_hex")), "bank 4 section 8 byte-prefix import");
            world.Apply(Event(0x1c9, [1, 2, 3, 4]));
            Check(world.WorldBanks.Current[0x1b4].ToPayload().SequenceEqual(bank), "incremental update preserves retained raw bank");
        }
        var sparse = new GoldWorldReplica(); var state = sparse.WorldBanks.ActualQuests;
        Check(state.KnownByteCount == 0 && !state.TryGetPayload(out _), "initial state is unknown");
        var reset = GoldActualQuestsCodec.CreateReset(2, 0x12345678);
        Check(reset.Sender == 2 && reset.Origin == 2 && reset.Sequence == 0 && reset.Payload.SequenceEqual(new byte[] { 0x78, 0x56, 0x34, 0x12 }), "native scalar producer");
        // Parsed metadata is deliberately inconsistent; only retained wire bytes may change state.
        sparse.Apply(new GoldActualQuestsResetEvent(reset, 0xffffffff));
        Check(state.KnownByteCount == 64 && !state.TryGetPayload(out _), "reset does not invent companion words");
        for (var slot = 0; slot < 16; slot++)
            Check(state.GetQuestWord(slot) == 0x12345678 && state.GetCompanionWord(slot) is null, "all sixteen first words only");
        sparse.Apply(Event(0x1ca, [0xaa, 0xbb, 0xcc, 0xdd, 0x11]));
        Check(state.KnownByteCount == 65 && state.GetQuestWord(0) == 0xddccbbaa && state.GetCompanionWord(0) is null, "partial companion stays unknown");
        sparse.Apply(Event(0x1ca, [0xaa, 0xbb, 0xcc, 0xdd, 0x11, 0x22, 0x33, 0x44]));
        Check(state.KnownByteCount == 68 && state.GetCompanionWord(0) == 0x44332211, "later prefix completes companion");
        sparse.Apply(Event(0x1ca, []));
        Check(state.KnownByteCount == 68 && state.GetCompanionWord(0) == 0x44332211, "empty full packet is a no-op");
        sparse.Clear();
        Check(state.KnownByteCount == 0 && state.GetQuestWord(0) is null && state.GetCompanionWord(0) is null, "disconnect clears knowledge");
        for (var length = 0; length <= 128; length++)
        {
            sparse.Clear(); sparse.Apply(Event(0x1ca, new byte[length]));
            Check(state.KnownByteCount == length, "every byte-length prefix accepted, including non-word lengths");
            Check(state.TryGetPayload(out _) == (length == 128), "only complete table exports known data");
        }
        foreach (var slot in new byte[] { 0, 17, 255 })
        {
            try { GoldActualQuestsCodec.CreateReset(slot, 0); throw new Exception("invalid sender accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }
        foreach (var slot in new[] { -1, 16 })
        {
            try { state.GetQuestWord(slot); throw new Exception("invalid table slot accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
            try { state.GetCompanionWord(slot); throw new Exception("invalid companion slot accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }
        Console.WriteLine($"PASS: {checks} actual-quest packet checks"); return checks;
    }
}
