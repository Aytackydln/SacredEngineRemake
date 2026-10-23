using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class PositionCorrectionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Position corrections: " + why); checks++; }
        using var prefixFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-state.json")));
        var prefix = Convert.FromHexString(prefixFixture.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-position-corrections.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("mode").GetString() == "server")
            {
                Check(row.GetProperty("relays").GetArrayLength() == (row.GetProperty("receiver_accepted").GetBoolean() ? 1 : 0), "native helper rejection suppresses relay");
                continue;
            }
            var op = row.GetProperty("opcode").GetUInt16(); var world = new GoldWorldReplica();
            var present = row.GetProperty("present").GetBoolean(); var creature = row.GetProperty("cast").GetBoolean();
            if (present) Seed(world, prefix, creature, row.GetProperty("base_before").GetUInt32(), row.GetProperty("creature_before").GetUInt32());
            var before = world.Objects.GetValueOrDefault(500u); var revision = world.Revision;
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            // Existing 0x146/0x165 codecs intentionally use canonical lengths; the native receiver also ignores suffixes.
            var entry = GoldGameEventDecoder.Decode(new(op, 0, 1, 3, op == 0x179 ? wire : wire[..24]));
            world.Apply(entry);
            var accepted = row.GetProperty("accepted").GetBoolean();
            Check(world.Revision == revision + (accepted ? 1 : 0), "native active/type/existence guards");
            if (!accepted)
            {
                Check(ReferenceEquals(before, world.Objects.GetValueOrDefault(500u)), "rejected correction does not replace actor"); continue;
            }
            var actor = world.Objects[500];
            Check(actor.BaseState.Flags == row.GetProperty("base_after").GetUInt32(), "native flight flag behavior");
            if (creature) Check(actor.Creature!.Word200 == row.GetProperty("creature_after").GetUInt32(), "native wake flag behavior");
            var deferred = row.GetProperty("deferred").GetBoolean();
            Check((actor.DeferredPosition is not null) == deferred, "native pending-slot presence");
            if (deferred)
            {
                Check(actor.DeferredPosition!.ToPayload().SequenceEqual(Convert.FromHexString(row.GetProperty("pending_hex").GetString()!)), "all 21 native slot bytes including padding and narrowed fields");
                Check(actor.BaseState == before!.BaseState && actor.FacingBits == before.FacingBits && actor.PositionVersion == before.PositionVersion, "deferred arrival leaves visible position and heading unchanged");
            }
            else
            {
                Check(actor.BaseState.X == -1234 && actor.BaseState.Y == 5678, "signed coordinates copied");
                Check(actor.BaseState.Surface == (byte)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(12)) &&
                    actor.BaseState.Sector == (ushort)BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(16)), "native surface/sector narrowing");
                Check(actor.FacingBits == BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(20)) && actor.PositionVersion == before!.PositionVersion+1, "heading bits and position generation");
                if (op == 0x179) Check(actor.Creature!.BaseState == actor.BaseState && actor.Physical!.BaseState == actor.BaseState, "current physical/creature position prefixes stay coherent");
            }
            if (op == 0x179)
                Check(ReferenceEquals(actor.Movement, before!.Movement) && actor.MovementVersion == before.MovementVersion && actor.AttackVersion == before.AttackVersion, "no action reset for 0x179");
            Check(actor.CurrentHitPoints == before!.CurrentHitPoints && world.CreatureControls.PendingReplies == 0, "no HP change or owner callback");
            world.Clear(); Check(world.Objects.Count == 0, "disconnect drops pending and visible state");
        }
        var safe = new GoldWorldReplica(); Seed(safe, prefix, false);
        var wrong = safe.Objects[500]; safe.Apply(GoldGameEventDecoder.Decode(GoldCreaturePositionCorrectionCodec.Create(2, 500, new(1, 2, 3, 4), 0)));
        Check(ReferenceEquals(wrong, safe.Objects[500]), "wrong-type 0x179 safely rejected rather than native null-cast read");
        return checks;
    }
    internal static void Seed(GoldWorldReplica world, byte[] prefix, bool creature = true, uint baseFlags = 0x42, uint creatureFlags = 0x1000)
    {
        var bytes = prefix.AsSpan(0, creature ? GoldCreatureStateLayout.PrefixSize : GoldPhysicalStateLayout.Size).ToArray();
        var basis = GoldObjectBaseState.FromPayload(bytes) with { ObjectReference = 500, Flags = baseFlags, X = 13, Y = 17, Surface = 3, Sector = 2 };
        basis.ToPayload().CopyTo(bytes, 0);
        if (creature) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), creatureFlags);
        world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16e, 2, bytes)));
        if (!creature) return;
        var movement = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(movement, 500);
        world.Apply(GoldGameEventDecoder.Decode(new GoldMessage(0x161, 2, 2, 0, movement)));
        var health = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(health, 500); BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(4), 777);
        world.Apply(GoldGameEventDecoder.Decode(new GoldMessage(0x128, 0, 1, 0, health)));
    }
}
