using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CreatureRelationshipChecks
{
    public static int Run(JsonElement rows)
    {
        var checks = 0;
        foreach (var row in rows.EnumerateArray())
        {
            uint Word(string key) => row.GetProperty(key).GetUInt32();
            var world = new GoldWorldReplica();
            world.ConfigureCreatureClasses(new Dictionary<uint, SacredCreatureClass>
            { [Word("a_type")] = (SacredCreatureClass)Word("a_class"), [Word("b_type")] = (SacredCreatureClass)Word("b_class") });
            GoldReplicatedObject Creature(string prefix)
            {
                var bytes = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.ReferenceOffset), Word(prefix+"_ref"));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.TypeOffset), Word(prefix+"_type"));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.RelationClassOffset), Word(prefix+"_class"));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word1F4Offset), Word(prefix+"_behavior"));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), Word(prefix+"_flags"));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word251Offset), Word(prefix+"_owner"));
                if (prefix == "b" && row.GetProperty("rider").GetBoolean())
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.EquipmentOffset+18*4), 3);
                world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 2, bytes)));
                return world.Objects[Word(prefix+"_ref")];
            }
            var first = Creature("a"); var second = Creature("b");
            if (row.GetProperty("linked").GetBoolean())
                first = first with { LinkedCreatures = GoldCreatureLinkState.FromPayload([..Words(Word("b_ref"),0),0]) };
            if (row.GetProperty("rider").GetBoolean())
            {
                var bytes = ((GoldObjectEvent)Actor()).ObjectState.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldObjectStateLayout.ReferenceOffset), 3);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.RelationClassOffset), 1);
                world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 2, bytes)));
            }
            world.Apply(Event(0x01E, Words(row.GetProperty("pvp").GetBoolean() ? 0x40u : 0)));
            var teams = new uint[18];
            if (Word("a_ref") <= 16) teams[Word("a_ref")-1] = Word("a_team");
            if (Word("b_ref") <= 16) teams[Word("b_ref")-1] = Word("b_team");
            world.Apply(Event(0x13A, Words(teams)));
            if (SacredNetworkCreatureRelations.AreEnemies(world, first, second) != row.GetProperty("result").GetBoolean())
                throw new Exception("Native health relationship mismatch: " + row);
            checks++;
        }
        return checks;
    }
}
