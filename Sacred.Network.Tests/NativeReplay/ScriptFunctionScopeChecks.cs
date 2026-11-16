using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.GameplayHost.Scripting;

internal static class ScriptFunctionScopeChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-function-scope.json")));
        var checks = 0;
        // Actor forwarding/casting is native-only evidence; these managed runs
        // compare quest scope without claiming a non-null factory implementation.
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("vm_result").GetInt32() != 1) continue;
            var seen = new List<uint>();
            WorldScriptExecutionContext context = null!;
            var code = Bytes(row, "code_hex");
            context = new(new(), [Empty(), Function(Bytes(row, "function_hex"))], code, true)
            {
                QuestActive = _ => row.GetProperty("active").GetBoolean(),
                BeforeCommand = _ => seen.Add(unchecked((uint)context.CurrentFunctionQuest!.Value))
            };
            new WorldScriptExecutor(context).Invoke(1);
            Check(seen.SequenceEqual(Quests(row)), "eligible commands see the native quest; rejected gates do not enter scope");
            Check(context.CurrentFunctionQuest is null, "ordinary completion preserves unknown outer scope");
        }
        foreach (var row in fixture.RootElement.GetProperty("restart_cases").EnumerateArray())
        {
            var seen = new List<uint>();
            WorldScriptExecutionContext context = null!;
            context = new(new(), [Empty(), Function(Bytes(row, "before_function_hex"))], Bytes(row, "code_hex"), true)
            {
                QuestActive = number => number != 9 || row.GetProperty("active").GetBoolean(),
                BeforeCommand = _ =>
                {
                    seen.Add(unchecked((uint)context.CurrentFunctionQuest!.Value));
                    if (seen.Count == 1) context.ReplaceFunction(1, Function(Bytes(row, "after_function_hex")));
                }
            };
            new WorldScriptExecutor(context).Invoke(1);
            Check(seen.SequenceEqual(Quests(row)), "restart reloads body/quest/deleted gates and bypasses initial name gate");
            Check(context.CurrentFunctionQuest == row.GetProperty("final_quest").GetInt32(), "restart overwrites saved quest even when the next gate rejects execution");
        }
        var nestedSeen = new List<int>();
        WorldScriptExecutionContext nested = null!;
        WorldScriptExecutor executor = null!;
        var inInner = false;
        nested = new(new(), [Empty(), new("outer", 0, 4, 7, 0, 0), new("inner", 0, 4, 9, 0, 0)], new byte[] { 62, 0, 4, 0 }, true)
        {
            QuestActive = _ => true,
            BeforeCommand = _ =>
            {
                nestedSeen.Add(nested.CurrentFunctionQuest!.Value);
                if (inInner) return;
                inInner = true;
                executor.Invoke(2);
                nestedSeen.Add(nested.CurrentFunctionQuest!.Value);
            }
        };
        executor = new(nested); executor.Invoke(1);
        Check(nestedSeen.SequenceEqual(fixture.RootElement.GetProperty("nested").GetProperty("events").EnumerateArray().Select(value => value.GetInt32())), "native nested quest save/restore");
        Check(nested.CurrentFunctionQuest is null, "nested completion restores unknown outer scope");

        WorldScriptExecutionContext failed = null!;
        failed = new(new(), [Empty(), new("failure", 0, 4, -1, 0, 0)], new byte[] { 62, 0, 4, 0 }, true)
        {
            BeforeCommand = _ => throw new InvalidDataException("Supplied native VM-failure boundary")
        };
        try { new WorldScriptExecutor(failed).Invoke(1); throw new Exception("Failed function accepted"); }
        catch (WorldScriptExecutionFailure)
        {
            Check(failed.IsInvalidated && failed.CurrentFunctionQuest is null, "VM failure restores caller quest and invalidates context");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native function scope: " + why); checks++; }
    }

    private static IEnumerable<uint> Quests(JsonElement row) => row.GetProperty("calls").EnumerateArray().Select(call => call.GetProperty("quest").GetUInt32());
    private static SacredScriptFunction Empty() => new("", 0, 0, -1, 0, 0);
    private static SacredScriptFunction Function(byte[] row) => new(Encoding.Latin1.GetString(row, 0, Array.IndexOf(row, (byte)0, 0, 64)),
        BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(64)), BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(68)),
        BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(72)), BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(76)),
        BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(80)));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
