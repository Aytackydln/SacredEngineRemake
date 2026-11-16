using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.GameplayHost.Scripting;

internal static class RegionWrapperChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-wrapper.json")));
        var data = fixture.RootElement;
        var functions = Enumerable.Repeat(new SacredScriptFunction("", 0, 0, -1, 0, 0), data.GetProperty("function_count").GetInt32()).ToArray();
        foreach (var row in data.GetProperty("functions").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetProperty("hex").GetString()!);
            functions[row.GetProperty("index").GetInt32()] = new(Encoding.Latin1.GetString(bytes.AsSpan(0, 64)).Split('\0')[0],
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(64)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(68)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(72)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(76)),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80)));
        }
        var index = data.GetProperty("index").GetUInt16();
        var body = functions[index];
        var code = new byte[functions.Max(function => function.Start + function.ByteLength)];
        Convert.FromHexString(data.GetProperty("code_hex").GetString()!).CopyTo(code, body.Start);
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var visited = new List<int>(); var calls = new List<int> { index };
            WorldScriptExecutionContext context = null!;
            context = new(new(), functions, code, true)
            {
                TraceCommand = trace =>
                {
                    if (trace.Script != "FunkCode.bin") return;
                    visited.Add(trace.Command.Opcode);
                    if (trace.Command.Opcode == 22)
                        calls.Add(Array.FindIndex(functions, function => WorldScriptNameComparer.Instance.Equals(function.Name,
                            SacredScriptOperandReader.Read(trace.Command)[0].Text!)));
                }
            };
            var value = row.GetProperty("value");
            if (value.ValueKind != JsonValueKind.Null) context.Variables.Set("DQ21_Quests", value.GetInt32());
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            if (row.GetProperty("startup").GetInt32() == 1)
            {
                // Public startup entry supplies the actual startup scope; the
                // host adapter below always invokes the ordinary callback path.
                var payload = Encoding.Latin1.GetBytes(body.Name + "\0");
                var call = new byte[payload.Length + 5]; call[0] = 22; call[2] = (byte)call.Length; call[4] = 1; payload.CopyTo(call, 5);
                var startupContext = new WorldScriptExecutionContext(new(), functions, code, true) { TraceCommand = context.TraceCommand };
                // The trace only resolves immutable function definitions above.
                if (value.ValueKind != JsonValueKind.Null) startupContext.Variables.Set("DQ21_Quests", value.GetInt32());
                new WorldScriptExecutor(startupContext).ExecuteStartup(call);
            }
            else Check(WorldScriptRegionCallbackProfile.TryInvoke(context, index, default, out _), "production wrapper profile accepts original body");
            Check(calls.SequenceEqual(row.GetProperty("calls").EnumerateArray().Select(number => number.GetInt32())), "original nested call order");
            // Opcode59 executes native branch scanning; managed control flow
            // consumes it rather than exposing it as a world command.
            Check(visited.SequenceEqual(row.GetProperty("visited").EnumerateArray().Select(number => number.GetInt32()).Where(opcode => opcode != 59)), "native predicate/command order");
            Check(context.Variables.Find("DQ21_Quests") == (value.ValueKind == JsonValueKind.Null ? null : value.GetInt32()) && !context.IsInvalidated,
                "no variable/world mutation or invalidation");
        }
        foreach (var row in data.GetProperty("gates").EnumerateArray())
        {
            var name = row.GetProperty("name").GetString()!;
            var context = new WorldScriptExecutionContext(new(), [new("", 0, 0, -1, 0, 0), new(name, 0, 0, -1, 0, 0)], ReadOnlyMemory<byte>.Empty, true);
            Check(WorldScriptEmptyFunctionResult.Invoke(context, 1, _ => new WorldScriptExecutor(context).Invoke(1)) == (row.GetProperty("returned").GetInt32() != 0),
                "exact dispatcher substring/prefix exclusion");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Region wrapper: " + why); checks++; }
    }
}
