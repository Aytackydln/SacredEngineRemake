using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptQuestObjectCompassChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-quest-object-compass.json")));
        var checks = 0;
        foreach (var sample in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var journal = WorldScriptQuestJournal.FromRuntime(Bytes(sample, "before_hex"), Enumerable.Repeat((byte)255, 128).ToArray());
            var quests = WorldScriptQuests.FromRuntime([], journal, sample.GetProperty("before_scratch").EnumerateArray().Select(value => value.GetUInt32()).ToArray());
            var lookups = new List<uint>();
            WorldScriptQuestObjectCompass.ApplyDeferred(quests, new(0, Bytes(sample, "command_hex")), sample.GetProperty("function_quest").GetInt32(),
                name => uint.Parse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture), resource =>
                {
                    lookups.Add(resource);
                    return sample.GetProperty("found").ValueKind == JsonValueKind.Null ? null : sample.GetProperty("found").GetUInt32();
                }, name => name.StartsWith("Res:", StringComparison.OrdinalIgnoreCase) && sample.GetProperty("resource_text").ValueKind != JsonValueKind.Null
                    ? sample.GetProperty("resource_text").GetString()! : name);
            Check(lookups.SequenceEqual(sample.GetProperty("lookups").EnumerateArray().Select(value => value.GetUInt32())), "actual parsed resource and no-target/sentinel gates");
            Check(quests.Scratch!.SequenceEqual(sample.GetProperty("scratch").EnumerateArray().Select(value => value.GetUInt32())), "all five deferred words match, including preserved coordinate words");
            Check(Enumerable.Range(0, journal.Count).SelectMany(journal.GetRecord).SequenceEqual(Bytes(sample, "after_hex")), "unmatched journal bytes remain exact");
        }
        var matching = new byte[372]; BinaryPrimitives.WriteInt32LittleEndian(matching.AsSpan(8), 10);
        var before = new uint[] { 11, 22, 33, 44, 55 };
        var state = WorldScriptQuests.FromRuntime([], WorldScriptQuestJournal.FromRuntime(matching, new byte[128]), before);
        var command = Convert.FromHexString("40001400015265733A3137303835000B0A000000");
        try { WorldScriptQuestObjectCompass.ApplyDeferred(state, new(0, command), _ => 17085, _ => throw new Exception("lookup preceded unsupported refresh guard")); }
        catch (NotSupportedException)
        {
            Check(state.Scratch!.SequenceEqual(before) && state.Journal.GetRecord(0).SequenceEqual(matching), "existing journal rejected before target lookup or any mutation");
            return checks;
        }
        throw new Exception("Unsupported existing-journal target refresh accepted.");
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native object compass: " + why); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
