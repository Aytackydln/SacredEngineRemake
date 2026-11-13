using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupHealerOperandChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-startup-healer-operands.json")));
        var checks = 0;
        var table = new WorldScriptRegistrationTable();
        table.Import(Bytes(fixture.RootElement, "registrations_hex").Chunk(80).Select(b => SacredScriptDialog.FromPayload(b)));
        foreach (var row in fixture.RootElement.GetProperty("operands").EnumerateArray())
        {
            var bytes = Bytes(row, "command_hex");
            var creation = SacredScriptCreatureCreationReader.Read(new(0, bytes), _ => throw new Exception("Unexpected position lookup"));
            Check(creation.PackedSpell == unchecked((uint)row.GetProperty("packed_spell").GetInt64()), "native ECS decode");
            Check(creation.HasExplicitBehavior == row.GetProperty("explicit").GetBoolean(), "explicit behavior gate");
            Check(WorldCreatureScriptDialog.Resolve(table, creation.DialogSelection) == row.GetProperty("slot").GetInt32(), "native registration slot");
        }
        foreach (var row in fixture.RootElement.GetProperty("bindings").EnumerateArray())
        {
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
            context.Registrations.Import(Bytes(row, "registrations_hex").Chunk(80).Select(b => SacredScriptDialog.FromPayload(b)));
            var before = Bytes(row, "before_hex"); var after = Bytes(row, "after_hex");
            var selected = row.GetProperty("selected").GetInt32();
            var actor = new WorldCreatureInitialActor(new GoldObjectBaseState(77,0,Word(before,20),0,0,0,0,0,0,0,0,0), null!,0,0,0,0,0,0,[]) { DialogIndex = Word(before,0x245) };
            actor = WorldCreatureScriptDialog.BindInitial(actor, context.Registrations, selected);
            Check(actor.DialogIndex == Word(after,0x245) && actor.Base.Flags == Word(after,20), "native actor dialog/flags");
            Check(context.Registrations.Count == row.GetProperty("count").GetInt32(), "attachment preserves count");
            var actual = Enumerable.Range(0, context.Registrations.Count).SelectMany(context.Registrations.GetRecord).ToArray();
            Check(actual.AsSpan().SequenceEqual(Bytes(row, "after_registrations_hex")), "all registration bytes");
            // Gold only changes dialog/flag words in the actor in this bounded startup block.
            var expected = before.ToArray();
            if (selected > 0) { BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(0x245), (uint)selected); BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(20), Word(before,20)|0x80000); }
            Check(expected.AsSpan().SequenceEqual(after), "native actor mutation boundary");
        }
        foreach (var row in fixture.RootElement.GetProperty("spell_writes").EnumerateArray())
        {
            var before = Bytes(row, "before_hex"); var expected = Bytes(row, "after_hex");
            var state = new SacredCreatureScriptSpellState(Half(before,0x25f),before[0x2b6],Half(before,0x261)).Apply(row.GetProperty("packed").GetUInt32());
            BinaryPrimitives.WriteUInt16LittleEndian(before.AsSpan(0x25f),state.Mode);before[0x2b6]=state.Selector;BinaryPrimitives.WriteUInt16LittleEndian(before.AsSpan(0x261),state.Parameter);
            Check(before.AsSpan().SequenceEqual(expected), "complete ECS write/preservation boundary");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Healer: " + why); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Half(byte[] bytes,int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
}
