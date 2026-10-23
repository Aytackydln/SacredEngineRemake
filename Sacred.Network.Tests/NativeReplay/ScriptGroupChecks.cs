using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

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
        return checks;

        void Compare(GoldWorldReplica world, JsonElement expected)
        {
            var actual = world.WorldBanks.ScriptGroups.Groups;
            Check(actual.Count == expected.EnumerateObject().Count(), "native map key count including empty groups");
            foreach (var group in expected.EnumerateObject())
                Check(actual[uint.Parse(group.Name)].SequenceEqual(group.Value.EnumerateArray().Select(v => v.GetUInt32())), "native ordered member words");
        }
    }

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
