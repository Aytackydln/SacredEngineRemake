using System.Buffers.Binary;
using System.Text.Json;
using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptQuestCompassChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root); var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Concat(fixture.RootElement.GetProperty("default_cases").EnumerateArray()))
        {
            var quests = Restore(row);
            var body = Bytes(row, "command_hex");
            var defaults = row.TryGetProperty("function_quest", out var functionQuest);
            SacredScriptFunction[] functions = defaults ? [new("", 0, 0, -1, 0, 0), new("compass", 0, body.Length, functionQuest.GetInt32(), 0, 0)] : [];
            var context = new WorldScriptExecutionContext(new(), functions, defaults ? body : ReadOnlyMemory<byte>.Empty, true)
                { Quests = quests, LocalPlayerSlot = row.GetProperty("owner").GetUInt32(), QuestActive = _ => true };
            new WorldScriptExecutor(context).ExecuteStartup(defaults ? new byte[] { 22, 0, 13, 0, 1 }.Concat(Encoding.ASCII.GetBytes("compass\0")).ToArray() : body);
            Check(Journal(quests).AsSpan().SequenceEqual(Bytes(row, "after_hex")), "all journal bytes, first match, zero-target refresh and radius");
            Check(quests.Journal.GetIndices().AsSpan().SequenceEqual(Bytes(row, "indices_hex")), "all sixteen untouched main/other index pairs");
            Check(quests.Scratch!.SequenceEqual(Words(row, "scratch")), "missing journal preserves middle scratch words; existing row leaves scratch unchanged");
            var events = row.GetProperty("steps").EnumerateArray().Where(step => step.GetProperty("kind").GetString() == "gui").ToArray();
            Check(context.LocalGuiRequests.SequenceEqual(events.Select(Request)), "raw controller slot, selected other/main quest and unchanged-target GUI suppression");
            Check(context.StartupCompleted && context.ExecutedCommands == (defaults ? 2 : 1) && context.CurrentFunctionQuest is null, "complete command with retained local events, suppressed publication and restored function scope");
            var parsed = Words(row, "parsed");
            if (unchecked((int)parsed[0]) < 0 || unchecked((int)parsed[1]) < 0) continue;
            // Observe the production submission boundary before either refresh or mutation.
            var observed = Restore(row); var calls = 0;
            WorldScriptQuestCompass.ApplyLiteral(observed, unchecked((int)parsed[2]), parsed[0], parsed[1], parsed[3],
                row.GetProperty("owner").GetUInt32(), request =>
                {
                    Check(calls < events.Length && request == Request(events[calls]) &&
                        Journal(observed).AsSpan().SequenceEqual(Bytes(events[calls], "journal_hex")), "GUI submission occurs before old-target refresh and writes");
                    calls++;
                });
            Check(calls == events.Length && Journal(observed).AsSpan().SequenceEqual(Bytes(row, "after_hex")), "prepared literal path agrees with complete native caller");
        }
        // Unknown function default and existing object/resource lookup fail before mutation or event submission.
        foreach (var target in new uint[] { uint.MaxValue, 0xEEEEEEEE })
        {
            var row = fixture.RootElement.GetProperty("installed"); var before = Bytes(row, "before_hex");
            BinaryPrimitives.WriteUInt32LittleEndian(before.AsSpan(16), target);
            var quests = WorldScriptQuests.FromRuntime([], WorldScriptQuestJournal.FromRuntime(before, new WorldScriptQuestJournal().GetIndices()), new uint[5]);
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true) { Quests = quests };
            try { new WorldScriptExecutor(context).ExecuteStartup(Bytes(row, "command_hex")); throw new Exception("Unverified object target accepted"); }
            catch (WorldScriptExecutionFailure) { Check(context.IsInvalidated && context.ExecutedCommands == 0 && context.LocalGuiRequests.Count == 0 && Journal(quests).AsSpan().SequenceEqual(before), "unverified old target is guarded before any effects"); }
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("QuestKompassPos: " + why); checks++; }
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = Read(root); var row = fixture.RootElement.GetProperty("installed");
        var journal = WorldScriptQuestJournal.FromRuntime(Bytes(row, "before_hex"), new WorldScriptQuestJournal().GetIndices());
        var quests = WorldScriptQuests.FromRuntime([], journal, new uint[5]);
        var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true) { Quests = quests, LocalPlayerSlot = 1 };
        new WorldScriptExecutor(context).ExecuteStartup(Bytes(row, "command_hex"));
        if (!journal.GetRecord(0).AsSpan().SequenceEqual(Bytes(row, "after_hex"))) throw new Exception("Uncompared compass output");
        return BitConverter.GetBytes(0u).Concat(journal.GetWireRecord(0)).ToArray();
    }
    private static WorldScriptQuests Restore(JsonElement row) => WorldScriptQuests.FromRuntime([], WorldScriptQuestJournal.FromRuntime(
        Bytes(row, "before_hex"), Bytes(row, "before_indices_hex")), Words(row, "before_scratch"));
    private static WorldScriptLocalGuiRequest Request(JsonElement row)
    {
        var words = Words(row, "words"); return new(words[0], words[1], words[2], words[3], words[4]);
    }
    private static byte[] Journal(WorldScriptQuests quests) => Enumerable.Range(0, quests.Journal.Count).SelectMany(quests.Journal.GetRecord).ToArray();
    private static uint[] Words(JsonElement row, string name) => row.GetProperty(name).EnumerateArray().Select(value => value.GetUInt32()).ToArray();
    private static byte[] Bytes(JsonElement row, string name) => Convert.FromHexString(row.GetProperty(name).GetString()!);
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-quest-compass.json")));
}
