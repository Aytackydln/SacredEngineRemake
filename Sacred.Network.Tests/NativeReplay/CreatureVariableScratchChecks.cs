using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class CreatureVariableScratchChecks
{
    // Actor construction/results have separate permanent comparisons. This suite isolates
    // the post-return scripting effect against complete native caller/constructor execution.
    private sealed class CompletedCreatureBoundary : IWorldScriptCreatureWorld
    {
        public void Create(WorldScriptExecutionContext context, SacredScriptCommand command) { }
    }
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-creature-variable-scratch.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
                { CreatureWorld = new CompletedCreatureBoundary(), VariableGlobals = new(new int[150]) };
            var code = Convert.FromHexString(row.GetProperty("command_hex").GetString()!)
                .Concat(Convert.FromHexString(row.GetProperty("variable_command_hex").GetString()!)).ToArray();
            new WorldScriptExecutor(context).ExecuteStartup(code);
            Check(context.StartupCompleted && context.VariableGlobals.UnresolvedIndexCount == 0, "bounded post-NPC suffix is established");
            Check(Convert.FromHexString(row.GetProperty("suffix_hex").GetString()!)[0] == 0, "actual native cleared owner byte");
            var actual = context.VariableGlobals.Apply();
            var expected = row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            for (var i = 0; i < 150; i++) Check(actual[i] == expected[i], $"complete native global word {i}");
        }
        foreach (var body in new[]
        {
            new byte[] { 0x63, (byte)'x', 0 },
            new byte[] { 1 }.Concat(Enumerable.Repeat((byte)'a', 128)).Append((byte)0).ToArray()
        })
        {
            var command = new byte[4 + body.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(command, 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(2), (ushort)command.Length);
            body.CopyTo(command, 4);
            var guarded = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
                { CreatureWorld = new CompletedCreatureBoundary() };
            new WorldScriptExecutor(guarded).ExecuteStartup(command.Concat(Convert.FromHexString("43000C000178000B07000000")).ToArray());
            Check(guarded.VariableGlobals.UnresolvedIndexCount == 1, "owner/long-text effects cannot establish a suffix via a supplied world boundary");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Creature suffix: " + why); checks++; }
    }
}
