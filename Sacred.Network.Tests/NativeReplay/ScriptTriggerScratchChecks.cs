using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.World;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Assets.GameBin;

internal static class ScriptTriggerScratchChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-trigger-scratch.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        foreach (var associated in new[]{false,true})
        {
            var source = new byte[268]; BinaryPrimitives.WriteUInt32LittleEndian(source, 0x01475254);
            var replica = new GoldWorldReplica();
            replica.Apply(new GoldReferenceRangeEvent(GoldReferenceCodec.CreateTriggerRangeRequest(0, 100, 1) with { Sender = 0 }, 17, 100));
            var registry = new WorldScriptTriggerRegistry(SacredWorldTriggerArchive.FromPayload(source), replica.TriggerReferences,
                SacredSectorIndex.FromPayload(new byte[256]).CreateGrid());
            var code=Convert.FromHexString(row.GetProperty("code_hex").GetString()!);
            var prefix=SacredCompiledScriptReader.Read(code).Where(command=>command.Opcode is 48 or 50).SelectMany(command=>command.Bytes.ToArray()).ToArray();
            WorldScriptExecutor executor=null!;var started=false;
            var context = new WorldScriptExecutionContext(new(), associated?[new("",0,0,-1,0,0),new("triggers",0,prefix.Length,-1,0,0)]:[], associated?prefix:ReadOnlyMemory<byte>.Empty, true)
            {
                WorldTriggers = registry,
                BeforeCommand=_=>{if(associated&&!started){started=true;executor.InvokeAssociatedCreature(1,17);}}
            };
            executor=new(context);
            executor.ExecuteStartup(associated?Convert.FromHexString("3e000400"):code);
            Check(context.StartupCompleted, "complete original sequence");
            Check(context.AssociatedCreatureReference==0,"trigger prefix restores associated caller");
            if(!associated)
            {
            Check(context.VariableGlobals.UnresolvedIndexCount == row.GetProperty("unresolved").GetInt32(), "patch suffix boundary");
            var storage = row.GetProperty("name_storage").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
            var actual = context.VariableGlobals.ApplyWithNameStorage(new int[150], (index, _) => storage[index]);
            var expected = row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            for (var i = 0; i < expected.Length; i++) Check(actual[i] == expected[i], $"native global word {i}");
            }
            var native = Convert.FromHexString(row.GetProperty("created_hex").GetString()!);
            var created = registry.Records[17];
            Check(created.Reference == BinaryPrimitives.ReadUInt32LittleEndian(native), "actual constructor reference");
            Check(created.Flags == BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(4)), "actual constructor flags");
            Check(created.Referent == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(6)), "actual constructor referent");
            Check(created.State == BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(10)), "actual constructor state");
            Check(created.Parameter == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(12)), "actual constructor parameter");
            Check(registry.Count == row.GetProperty("trigger_count").GetUInt32(), "native vector size including sparse slots");
            Check(registry.Find("Trgtrigger") == 17, "original name registration binding");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Trigger scratch: " + why); checks++; }
    }
}
