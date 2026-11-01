using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class ScriptGroupChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script groups: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-groups.json")));
        foreach (var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Bank(Groups(row.GetProperty("initial")))));
            var retained = world.WorldBanks.Current[0x1b3].ToPayload();
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var original = bytes.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(0x18f, 0, 31, 7, bytes)); Array.Fill(bytes, (byte)0xcc);
            world.Apply(entry); Compare(world, row.GetProperty("groups"));
            Check(entry is GoldScriptGroupEvent && entry.Message.Payload.SequenceEqual(original), "owned wire bytes and ignored suffix");
            Check(world.WorldBanks.Current[0x1b3].ToPayload().SequenceEqual(retained), "incremental updates preserve retained bank bytes");
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no object lookup guard or invented callback");
            world.Clear(); Check(world.WorldBanks.ScriptGroups.Groups.Count == 0, "disconnect clears groups");
        }
        foreach (var row in fixture.RootElement.GetProperty("bank_cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Bank(Groups(row.GetProperty("initial")))));
            world.Apply(GoldGameEventDecoder.Decode(Bank(Groups(row.GetProperty("records")))));
            Compare(world, row.GetProperty("groups"));
        }
        foreach (var row in fixture.RootElement.GetProperty("startup_cases").EnumerateArray())
            Check(row.GetProperty("dispatch_flags").EnumerateArray().All(flag => flag.GetByte() == 1) &&
                row.GetProperty("after").GetByte() == 1, "native startup dispatch establishes group publication suppression");
        foreach (var row in fixture.RootElement.GetProperty("publication_cases").EnumerateArray())
        {
            var sends = row.GetProperty("networking").GetInt32() != 0 && row.GetProperty("suppressed").GetInt32() == 0 &&
                row.GetProperty("publish").GetInt32() != 0 && (row.GetProperty("group").GetUInt32() & 0xffff) != 0;
            Check(sends == row.GetProperty("constructs_packet").GetBoolean(), "native group outgoing gates before packet construction");
        }
        foreach (var row in fixture.RootElement.GetProperty("creation_cases").EnumerateArray())
        {
            var groups = new WorldScriptGroups();
            foreach (var initial in Groups(row.GetProperty("initial")))
                foreach (var member in initial.Members) groups.AssignInitial(Actor(member), unchecked((int)initial.Key));
            var actor = Actor(row.GetProperty("member").GetUInt32()) with { ScriptGroup = 99 };
            var before = actor;
            for (var repeat = 0; repeat < row.GetProperty("repeat").GetInt32(); repeat++)
                actor = groups.AssignInitial(actor, unchecked((int)row.GetProperty("group").GetUInt32()));
            Check(actor.ScriptGroup == row.GetProperty("actor_group").GetUInt32(), "native unaligned actor group word");
            Check(actor with { ScriptGroup = before.ScriptGroup } == before && ReferenceEquals(actor.Node, before.Node),
                "initial group preserves all other constructed actor state and lifetime");
            var expected = row.GetProperty("groups");
            Check(groups.Groups.Count == expected.EnumerateObject().Count(), "initial native map key count");
            foreach (var group in expected.EnumerateObject())
                Check(groups.Groups[uint.Parse(group.Name)].SequenceEqual(group.Value.EnumerateArray().Select(value => value.GetUInt32())),
                    "initial native ordered members and duplicate suppression");
        }
        return checks;

        void Compare(GoldWorldReplica world, JsonElement expected)
        {
            var actual = world.WorldBanks.ScriptGroups.Groups;
            Check(actual.Count == expected.EnumerateObject().Count(), "native map key count including empty groups");
            foreach (var group in expected.EnumerateObject())
                Check(actual[uint.Parse(group.Name)].SequenceEqual(group.Value.EnumerateArray().Select(v => v.GetUInt32())), "native ordered member words");
        }
    }

    private static WorldCreatureInitialActor Actor(uint reference) => new(
        new(reference, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new(new byte[SacredActorValuesLayout.Size], new byte[GoldCreatureValuesLayout.Block128Size], 6, 0),
        0, 0, 0, 0, 0, 0, []);

    private static (uint Key, uint[] Members)[] Groups(JsonElement rows) => rows.EnumerateArray()
        .Select(row => (row[0].GetUInt32(), row[1].EnumerateArray().Select(value => value.GetUInt32()).ToArray())).ToArray();

    internal static GoldMessage Bank(params (uint Key, uint[] Members)[] groups) => ScriptResourceChecks.Bank(0x1b3, 0,
        groups.Select(group => Words([group.Key, (uint)group.Members.Length, ..group.Members])).ToArray());

    internal static byte[] Words(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }
}
