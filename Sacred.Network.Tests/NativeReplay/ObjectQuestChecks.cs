using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ObjectQuestChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        using var creatures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-state.json")));
        var prefix = Convert.FromHexString(creatures.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!);
        GoldGameEvent Actor(byte[] source, uint flags = 0x80040, byte owner = 2)
        {
            var body = source.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.ReferenceOffset), 500);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldObjectStateLayout.FlagsOffset), flags);
            body[GoldObjectStateLayout.OwnerOffset] = owner;
            if (body.Length >= GoldPhysicalStateLayout.Size)
            {
                BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(GoldPhysicalStateLayout.QuestNumberOffset), 111);
                BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(GoldPhysicalStateLayout.QuestEffectOffset), 222);
            }
            return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16e, 1, body));
        }
        GoldMessage Raw(ushort opcode, uint raw)
        {
            var body = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(body, 500);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), raw);
            return new(opcode, 0, 1, 0, body);
        }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-quest.json")));
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            if (row.GetProperty("present").GetBoolean()) world.Apply(Actor(row.GetProperty("cast").GetBoolean() ? prefix : prefix[..GoldObjectStateLayout.Size],
                row.GetProperty("inactive").GetBoolean() ? 0x80050u : 0x80040u, row.GetProperty("owner").GetByte()));
            var before = world.Objects.GetValueOrDefault(500u); var revision = world.Revision;
            world.Apply(GoldGameEventDecoder.Decode(Raw(row.GetProperty("opcode").GetUInt16(), row.GetProperty("raw_value").GetUInt32())));
            Check(world.Revision == revision + (row.GetProperty("accepted").GetBoolean() ? 1 : 0), "native object/3D/inactive guards");
            Check(world.CreatureControls.PendingReplies == 0, "quest receipt must not publish creature callback");
            if (world.Objects.GetValueOrDefault(500u)?.Physical is not { } physical) continue;
            var after = world.Objects[500];
            Check(physical.QuestNumber == row.GetProperty("number").GetInt16() && physical.QuestEffect == row.GetProperty("effect").GetInt16(), "native signed narrowing even outside quest bank");
            Check(after.Creature!.PhysicalState.QuestNumber == physical.QuestNumber && after.Creature.PhysicalState.QuestEffect == physical.QuestEffect, "creature physical layer agrees");
            var expected = before!.Creature!.ToPayload();
            BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(GoldPhysicalStateLayout.QuestNumberOffset), physical.QuestNumber);
            BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(GoldPhysicalStateLayout.QuestEffectOffset), physical.QuestEffect);
            Check(expected.SequenceEqual(after.Creature.ToPayload()), "quest tags change no other bytes");
            Check(after.BaseState == before.BaseState && after.InstanceVersion == before.InstanceVersion && after.MovementVersion == before.MovementVersion && after.AttackVersion == before.AttackVersion, "quest tags preserve lifetime/actions");
            Check(before.Physical!.QuestNumber == 111 && before.Physical.QuestEffect == 222, "immutable prior state");
        }
        foreach (var row in native.RootElement.GetProperty("producers").EnumerateArray())
        {
            var value = unchecked((short)row.GetProperty("input").GetUInt32());
            var message = row.GetProperty("opcode").GetUInt16() == 0x173 ? GoldObjectQuestCodec.CreateNumber(2, 500, value) : GoldObjectQuestCodec.CreateEffect(2, 500, value);
            Check(BitConverter.ToUInt32(message.Payload, 4) == row.GetProperty("wire_value").GetUInt32(), "native producer sign extension");
            Check(message.Sender == 2 && message.Origin == 2, "producer identity");
        }
        foreach (ushort op in new ushort[] { 0x173, 0x175 })
        {
            for (var length = 0; length < 18; length++)
            {
                if (length == 8) continue;
                try { GoldGameEventDecoder.Decode(new(op, 0, 1, 0, new byte[length])); throw new Exception("Malformed quest packet accepted"); }
                catch (InvalidDataException) { checks++; }
            }
            var message = Raw(op, 0xffff); var decoded = (GoldObjectQuestEvent)GoldGameEventDecoder.Decode(message);
            message.Payload[4] = 0;
            Check(decoded.Value == -1 && decoded.Message.Payload[4] == 0xff, "decoder owns payload");
        }
        var session = new GoldWorldReplica(); session.Apply(Actor(prefix));
        session.Apply(GoldGameEventDecoder.Decode(GoldCreatureControlCodec.CreateFightMode(1, 500, 0x8000)));
        session.Apply(GoldGameEventDecoder.Decode(GoldCreatureControlCodec.CreateDialog(1, 500, 42, 0x88040)));
        session.CreatureControls.DiscardReplies(); var current = session.Objects[500];
        session.Apply(new GoldObjectQuestEvent(Raw(0x173, 0xffff), 999, 123));
        session.Apply(GoldGameEventDecoder.Decode(Raw(0x175, 0x8000)));
        Check(session.Objects[500].Creature!.IsInFightMode && session.Objects[500].Creature!.DialogIndex == 42 && session.Objects[500].BaseState.Flags == 0x88040, "newer control state survives physical patch");
        Check(session.Objects[500].Physical!.QuestNumber == -1 && session.Objects[500].Physical!.QuestEffect == short.MinValue, "wire is authoritative over forged typed fields");
        Check(current.Physical!.QuestNumber == 111 && current.Creature!.DialogIndex == 42, "prior layered state stays immutable");
        var frozen = session.Objects[500];
        try { GoldGameDispatcher.Dispatch([Raw(0x173, 7), new(0x175, 0, 1, 0, new byte[9])], session.Apply); throw new Exception("Malformed batch accepted"); }
        catch (InvalidDataException) { Check(session.Objects[500] == frozen, "batch validates before mutation"); }
        session.Apply(GoldGameEventDecoder.Decode(new(0x166, 0, 1, 0, BitConverter.GetBytes(500u))));
        session.Apply(GoldGameEventDecoder.Decode(Raw(0x173, 8)));
        Check(session.Objects[500].Physical!.QuestNumber == -1, "late tag cannot mutate removed object");
        session.Apply(Actor(prefix)); Check(session.Objects[500].Physical!.QuestNumber == 111 && session.Objects[500].InstanceVersion != frozen.InstanceVersion, "reference reuse uses replacement tags");
        session.Clear(); session.Apply(GoldGameEventDecoder.Decode(Raw(0x173, 99))); session.Apply(Actor(prefix));
        Check(session.Objects[500].Physical!.QuestNumber == 111, "no queued tag across disconnect/missing object");

        using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-item-state.json")));
        var item = GoldItemState.FromPayload(Convert.FromHexString(items.RootElement.GetProperty("samples")[0].GetProperty("payload_hex").GetString()!));
        var container = GoldContainerState.Create(item, new uint[] { 19, 27 });
        var containerEvent = Actor(container.ToPayload()); var itemType = GoldObjectBaseState.FromPayload(container.ToPayload()).ObjectType;
        var containers = new GoldWorldReplica(); containers.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [itemType] = SacredItemCategory.Container }); containers.Apply(containerEvent);
        containers.Apply(GoldGameEventDecoder.Decode(Raw(0x173, 0xffff))); containers.Apply(GoldGameEventDecoder.Decode(Raw(0x175, 32768)));
        var box = containers.Objects[500];
        Check(box.Item!.PhysicalState.QuestNumber == -1 && box.Container!.Item.PhysicalState.QuestNumber == -1 && box.Container.Item.PhysicalState.QuestEffect == short.MinValue, "item/container nested physical state agrees");
        Check(box.Container!.Count == 2 && box.Container.GetWord(0) == 19 && box.Container.GetWord(1) == 27, "container extension survives");
        session = new(); session.Apply(Actor(prefix[..GoldPhysicalStateLayout.Size])); session.Apply(GoldGameEventDecoder.Decode(Raw(0x175, 0xffff)));
        Check(session.Objects[500] is { Creature: null, Item: null, Physical.QuestEffect: -1 }, "ordinary physical props accept quest tags");

        using var objects = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-state.json")));
        foreach (var row in objects.RootElement.GetProperty("samples").EnumerateArray().Where(r => r.GetProperty("method").GetString() == "0x450140"))
        {
            var memory = Convert.FromHexString(row.GetProperty("state_hex").GetString()!);
            var physical = GoldPhysicalState.FromPayload(Convert.FromHexString(row.GetProperty("payload_hex").GetString()!));
            Check(physical.QuestNumber == BitConverter.ToInt16(memory, 0x94) && physical.QuestEffect == BitConverter.ToInt16(memory, 0x96), "native writer maps quest tags to wire offsets");
        }
        foreach (short number in new short[] { -1, short.MinValue, 0, 3, short.MaxValue })
        foreach (short effect in new short[] { -1, short.MinValue, 0, 123, short.MaxValue })
        {
            var saved = new byte[SacredSavedPhysicalLayout.Size];
            BinaryPrimitives.WriteInt16LittleEndian(saved.AsSpan(SacredSavedPhysicalLayout.QuestNumberOffset), number);
            BinaryPrimitives.WriteInt16LittleEndian(saved.AsSpan(SacredSavedPhysicalLayout.QuestEffectOffset), effect);
            var physical = GoldSavedPhysicalStateImporter.ImportRetainedState(saved);
            Check(physical.QuestNumber == number && physical.QuestEffect == effect, "saved signed quest tags survive upload projection");
        }
        Console.WriteLine($"PASS: {checks} object quest tag checks."); return checks;
    }
}
