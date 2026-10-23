using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

internal static class ScriptDefinitionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script definitions: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-definitions.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var after = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = wire.ToArray();
            var records = Enumerable.Range(0, before.Length / 100).Select(i => SacredScriptDefinition.FromPayload(before.AsSpan(i * 100, 100))).ToArray();
            var world = new GoldWorldReplica(); var state = world.WorldBanks.Definitions;
            state.Configure(records);
            var entry = GoldGameEventDecoder.Decode(new(row.GetProperty("opcode").GetUInt16(), 0, 1, 2, wire));
            Array.Fill(wire, (byte)0xcc); var revision = world.Revision; world.Apply(entry);
            Check(state.Records.SelectMany(r => r.ToPayload()).SequenceEqual(after), "native complete record bytes, order and vector count");
            Check(entry.Message.Payload.SequenceEqual(expected), "owned wire and retained suffix");
            Check(world.Revision == revision+1 && state.PendingChangeCount == 2, "ordered baseline and delta");
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no actor or outgoing callback effect");
            if (state.Records.Count != 0)
            {
                var exported = state.Records[0].ToPayload(); exported[0] ^= 255;
                Check(state.Records[0].ToPayload().SequenceEqual(after.AsSpan(0, 100).ToArray()), "immutable exported record");
            }
            var late = new GoldWorldReplica(); late.Apply(entry);
            Check(!late.WorldBanks.Definitions.IsInitialized && late.WorldBanks.Definitions.PendingCount == 1, "receipt waits for original baseline");
            late.WorldBanks.Definitions.Configure(records);
            Check(late.WorldBanks.Definitions.Records.SelectMany(r => r.ToPayload()).SequenceEqual(after), "late baseline produces native result");
            Check(late.WorldBanks.Definitions.PendingCount == 0, "pending receipt resolved");
            world.Clear();
            Check(!state.IsInitialized && state.Records.Count == 0 && state.PendingChangeCount == 0 && state.PendingCount == 0, "disconnect cleanup");
        }
        return checks;
    }
}
