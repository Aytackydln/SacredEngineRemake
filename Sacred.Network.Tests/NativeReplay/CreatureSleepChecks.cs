using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class CreatureSleepChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-sleep.json")));
        using var prefixes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-state.json")));
        var prefix = Convert.FromHexString(prefixes.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
        GoldObjectEvent Actor(bool creature = true, bool inactive = false)
        {
            var body = creature ? prefix.ToArray() : prefix[..GoldObjectStateLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.ReferenceOffset), 500);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.FlagsOffset), inactive ? 0x10u : 0);
            if (creature) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.Word200Offset), 0x1000);
            return (GoldObjectEvent)GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E, 1, body));
        }
        GoldMessage Sleep(uint offset) => new(0x126, 0, 7, 4,
            BitConverter.GetBytes(500u).Concat(BitConverter.GetBytes(offset)).ToArray());
        foreach (var sample in fixtures.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            if (sample.GetProperty("present").GetBoolean()) world.Apply(Actor(sample.GetProperty("cast").GetBoolean(), sample.GetProperty("inactive").GetBoolean()));
            var entry = (GoldCreatureSleepEvent)GoldGameEventDecoder.Decode(Sleep(BitConverter.SingleToUInt32Bits(sample.GetProperty("offset").GetSingle())));
            var previousVersion = world.Objects.GetValueOrDefault(500u)?.AttackVersion ?? 0;
            world.Apply(entry);
            var accepted = sample.GetProperty("accepted").GetBoolean();
            Check(world.Combat.SleepEvents == (accepted ? 1 : 0), "native missing/type/inactive guards without an owner restriction");
            if (!accepted) continue;
            var actor = world.Objects[500];
            Check(actor.Creature!.Word200 == sample.GetProperty("flags").GetUInt32(), "native sleep bit preserves other flags");
            Check(actor.AttackVersion == previousVersion + 1 && actor.Movement is null, "sleep starts an independently versioned action");
            Check(entry.ClockOffsetBits == BitConverter.SingleToUInt32Bits(sample.GetProperty("offset").GetSingle()), "native clock operand retained exactly");
            // KI/EI reset/motion exclusions are reported by the native fixture; these modes
            // are absent from this network snapshot and are not fabricated by the replica.
        }
        foreach (var bits in new uint[] { 0, 0x80000000, 0x7FC12345, 0x7F800000, 0xFF800000 })
        {
            var wire = Sleep(bits);
            var decoded = (GoldCreatureSleepEvent)GoldGameEventDecoder.Decode(wire);
            wire.Payload[4] ^= 1;
            Check(decoded.ClockOffsetBits == bits && BitConverter.ToUInt32(decoded.Message.Payload, 4) == bits, "all float bit patterns retained in an isolated payload");
        }
        for (var size = 0; size <= 12; size++)
        {
            if (size == 8) continue;
            try { GoldGameEventDecoder.Decode(new(0x126, 0, 0, 0, new byte[size])); throw new Exception("Invalid sleep size accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        var replica = new GoldWorldReplica(); replica.Apply(Actor());
        var before = replica.Objects[500]; var frozen = before.Creature!.ToPayload();
        var hp = new GoldMessage(0x128, 0, 0, 0, BitConverter.GetBytes(500u).Concat(BitConverter.GetBytes(1234)).ToArray());
        replica.Apply(GoldGameEventDecoder.Decode(hp));
        replica.Apply(GoldGameEventDecoder.Decode(Sleep(0x3F400000)));
        var sleeping = replica.Objects[500];
        Check(sleeping.Creature!.IsSleeping && sleeping.CurrentHitPoints == 1234 && sleeping.InstanceVersion == before.InstanceVersion, "sleep preserves health and object lifetime");
        Check(before.Creature.ToPayload().SequenceEqual(frozen) && !before.Creature.IsSleeping, "received source snapshot is immutable");
        replica.Apply(GoldGameEventDecoder.Decode(Sleep(0)));
        Check(replica.Objects[500].AttackVersion == sleeping.AttackVersion + 1 && replica.Combat.SleepEvents == 2, "repeated sleep restarts the action");
        var state = new byte[GoldCreatureUpdateLayout.StateSize];
        BinaryPrimitives.WriteUInt32LittleEndian(state, GoldCreatureStateLayout.Marker);
        BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(GoldCreatureUpdateLayout.Word200Offset), 0x1000);
        var wake = GoldCreatureUpdateCodec.CreateState(1, 500, sleeping.BaseState.ObjectType, GoldCreatureUpdateState.FromPayload(state));
        replica.Apply(GoldGameEventDecoder.Decode(wake));
        Check(!replica.Objects[500].Creature!.IsSleeping, "native 0x11A merge clears the sleeping flag");
        var replacement = Actor(); replica.Apply(replacement);
        Check(!replica.Objects[500].Creature!.IsSleeping, "full snapshot replaces old sleep state");
        replica.Apply(GoldGameEventDecoder.Decode(Sleep(0)));
        replica.Apply(GoldGameEventDecoder.Decode(new(0x167, 0, 0, 0, BitConverter.GetBytes(500u))));
        replica.Apply(Actor());
        Check(!replica.Objects[500].Creature!.IsSleeping && replica.Objects[500].InstanceVersion != before.InstanceVersion, "reference reuse cannot inherit sleep");
        var revision = replica.Revision;
        try
        {
            GoldGameDispatcher.Dispatch([Sleep(0), new(0x126, 0, 0, 0, new byte[7])], replica.Apply);
            throw new Exception("Malformed sleep batch accepted");
        }
        catch (InvalidDataException) { Check(replica.Revision == revision && !replica.Objects[500].Creature!.IsSleeping, "malformed trailing sleep preserves batch atomicity"); }
        replica.Clear();
        Check(replica.Combat.SleepEvents == 0 && replica.Combat.Recent.Count == 0 && replica.Objects.Count == 0, "disconnect clears sleep state and history");
        Console.WriteLine($"PASS: {checks} creature sleep decode, native flags/guards, wake, lifetime and atomic-batch checks.");
        return checks;
    }
}
