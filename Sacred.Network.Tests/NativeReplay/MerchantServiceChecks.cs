using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class MerchantServiceChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-merchant-services.json")));
        var definitions = SacredCreatureScriptServiceDefinitions.Read(Convert.FromHexString(fixture.RootElement.GetProperty("definition_code_hex").GetString()!));
        var checks = 0;
        void Check(bool value,string message) { if (!value) throw new Exception(message); checks++; }
        Check(definitions.HorseMerchantDialog==0x2bf20 && definitions.HorseMerchantFlags==0x4000000 && definitions.DialogObjectFlags==0x80000,
            "Original executable supplies the HorseMerchant service flags and dialog selector.");
        Check(definitions.TraderFlags==0x2000 && definitions.ComboMasterFlags==0x4000 && definitions.SmithFlags==0x1000,"Original merchant service flags");
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before=Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var after=Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var mode=row.GetProperty("mode").GetUInt32();
            Check(row.GetProperty("notifications").EnumerateArray().Select(value=>value.GetUInt32()).SequenceEqual(
                (mode&32)!=0 && !row.GetProperty("startup").GetBoolean() ? new uint[] { 0x11a } : []),
                "Actual dialog setter suppresses startup notification; live mode positive control requests 0x11A.");
            if (!row.GetProperty("startup").GetBoolean()) continue;
            var actor=new WorldCreatureInitialActor(new(17,288,Word(before,20),0,0,0,0,0,0,0,1,0),
                new(new byte[238],new byte[24],0,0),0,0,0,0,0,0,[])
                { RuntimeFlags=Word(before,0x200),DialogIndex=Word(before,0x245) };
            var result=WorldCreatureScriptServices.ApplyInitial(actor,mode,definitions);
            Check(result.Base.Flags==Word(after,20) && result.RuntimeFlags==Word(after,0x200) && result.DialogIndex==Word(after,0x245),
                "Shared startup service matches all actual native service/dialog writes with zero, A5 and random input.");
            Check(result.Base with { Flags=actor.Base.Flags } == actor.Base && ReferenceEquals(result.Component,actor.Component) && result.Node==actor.Node,
                "Service setup preserves unrelated identity, constructor values and scheduler lifetime.");
        }
        foreach(var row in fixture.RootElement.GetProperty("name_flag_cases").EnumerateArray())
        {
            var before=Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var after=Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var actor=new WorldCreatureInitialActor(default,new(new byte[238],new byte[24],0,0),0,0,0,0,0,0,[]) { RuntimeFlags=Word(before,0x200) };
            var result=WorldCreatureScriptNameFlags.Apply(actor,row.GetProperty("resource").GetUInt32());
            Check(result.RuntimeFlags==Word(after,0x200) && result.Base==actor.Base && ReferenceEquals(result.Component,actor.Component),
                "Native pre-result unnamed/sentinel resource flag uses the signed predicate and preserves unrelated state.");
        }
        return checks;
    }
    private static uint Word(byte[] bytes,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
