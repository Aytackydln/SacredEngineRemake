using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

internal static class StartupDialogStateChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-dialog-state");using var owners=Read("native-startup-owner");
        using var associated=Read("native-startup-associated-dialog");
        using var constructor=Read("native-horse-constructor");using var basis=Read("native-creature-base-results");
        using var initialization=Read("native-creature-result-initialization");using var finalization=Read("native-creature-result-finalization");
        using var precision=Read("native-startup-floating-point");using var levels=Read("native-creature-level-selection");
        var image=new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var source in new[]{constructor,basis,initialization,finalization,precision})
            foreach(var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var balance=Bytes(constructor.RootElement,"balance_hex");var results=WorldCreatureScalarResultAssets.Read(image,balance);
        var archive=new byte[256+CreatureGoldRecordLayout.Size*2];BinaryPrimitives.WriteUInt32LittleEndian(archive,0x00464943);BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),2);
        Bytes(owners.RootElement.GetProperty("templates"),"555").CopyTo(archive,256+CreatureGoldRecordLayout.Size);
        var template=CreaturePakArchive.Parse(archive).Entries[1];var data=EquipmentGenerationFixture.ReadStartup(root);var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Concat(associated.RootElement.GetProperty("cases").EnumerateArray()))
        {
            var before=Actor(Bytes(row,"before_hex"));
            var table=new WorldScriptRegistrationTable();table.Import(Bytes(row,"before_table_hex").Chunk(80).Select(bytes=>Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(bytes)));
            var command=new Sacred.Core.GameBin.Scripts.SacredScriptCommand(0,Bytes(row,"command_hex"));
            var operands=Sacred.Assets.GameBin.SacredScriptOperandReader.Read(command);var operand=operands[1];
            var selection=new Sacred.Core.GameBin.Scripts.SacredScriptDialogSelection(operand.Text,
                operand.Kind==Sacred.Core.GameBin.Scripts.SacredScriptArgumentKind.DialogRegistrationNumber?checked((ushort)operand.Integer!.Value):null);
            WorldCreatureInitialActor next=null!;
            var reference=row.TryGetProperty("actor_reference",out var actorReference)?actorReference.GetUInt32():0;
            if(reference==0)next=WorldCreatureScriptDialogState.Apply(before,table,selection);
            else
            {
                var bytes=Bytes(row,"command_hex");
                var context=new Sacred.GameplayHost.Scripting.WorldScriptExecutionContext(new(),
                    [new("",0,0,-1,0,0),new("dialog",0,bytes.Length,-1,0,0)],bytes,true);
                new WorldScriptExecutor(context,(scope,_)=>
                {
                    Check(scope.AssociatedCreatureReference==reference,"native explicit target with retained associated caller");
                    next=WorldCreatureScriptDialogState.Apply(before,table,selection);return true;
                }).InvokeAssociatedCreature(1,reference);
                Check(context.AssociatedCreatureReference==0&&!context.IsInvalidated,"named dialog invocation restores caller scope");
            }
            var expected=Bytes(row,"after_hex");
            Block(Enumerable.Range(0,table.Count).SelectMany(table.GetRecord).ToArray(),Bytes(row,"table_hex"),"all native registration bytes / duplicate first match / prior detach");
            Check(next.Base.Flags==Word(expected,20)&&next.DialogIndex==Word(expected,0x245)&&next.KiMode==Short(expected,0x25f),"native dialog flag/slot, missing-name clear and preserved KI mode");
            Check(row.GetProperty("rng_before").GetUInt32()==row.GetProperty("rng_after").GetUInt32()&&row.GetProperty("publications").GetArrayLength()==1,"actual no-RNG / exactly one explicit dialog publication");
            // Nonzero KI is a native positive control; result composition/serialization remains guarded.
            if(before.KiMode!=0)continue;
            var initial=WorldCreatureInitialSerialization.Write(next,data.Templates.GetItem(next.Base.ObjectType),new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem);
            var update=GoldCreatureUpdateCodec.CreateState(1,next.Base.ObjectReference,next.Base.ObjectType,GoldCreatureUpdateState.FromCreature(initial));
            var publication=row.GetProperty("publications")[0];
            Check(update.Opcode==publication.GetProperty("opcode").GetUInt16(),"startup dialog setter requests 0x11A before result recomputation");
            Block(update.Payload,Bytes(publication,"prefix_hex").Concat(Bytes(publication,"payload_hex")).ToArray(),"complete actual unsuppressed dialog update");
            next=WorldCreatureEquippedResults.Apply(next,template,results,data,new Dictionary<uint,WorldEquipmentInitialItem>(),1,1,true,0);
            next=next with {Component=next.Component with {ResultValid=1}};
            Block(next.Component.Values,expected.AsSpan(0x3b0,238),"complete dialog-only values / retained price");
            Block(next.Component.Health,expected.AsSpan(0x4d0,24),"complete dialog-only health");
            Check(next.Base==(before.Base with {Flags=Word(expected,20)})&&next.PartnerReference==before.PartnerReference&&next.Component.ResultValid==expected[0x4cf],"preserved owner/partner and native validity");
            var full=WorldCreatureInitialSerialization.Write(next,data.Templates.GetItem(next.Base.ObjectType),new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem).ToPayload();
            var native=Bytes(row.GetProperty("final_publication"),"payload_hex");
            Block(full.AsSpan(0,109),native.AsSpan(0,109),"complete current stream prefix");
            Block(full.AsSpan(121),native.AsSpan(121),"complete current stream tail");
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("Dialog-only state: "+why);checks++;}
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string why){if(!actual.SequenceEqual(expected))throw new Exception("Dialog-only state: "+why);checks+=actual.Length;}
    }
    public static byte[] WireExample(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-dialog-state.json")));
        var row=fixture.RootElement.GetProperty("cases")[0];var data=EquipmentGenerationFixture.ReadStartup(root);
        var table=new WorldScriptRegistrationTable();table.Import(Bytes(row,"before_table_hex").Chunk(80).Select(bytes=>Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(bytes)));
        var next=WorldCreatureScriptDialogState.Apply(Actor(Bytes(row,"before_hex")),table,new("auftrag22",null));
        var initial=WorldCreatureInitialSerialization.Write(next,data.Templates.GetItem(next.Base.ObjectType),new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem);
        var packet=GoldCreatureUpdateCodec.CreateState(1,next.Base.ObjectReference,next.Base.ObjectType,GoldCreatureUpdateState.FromCreature(initial));
        var expected=row.GetProperty("publications")[0];
        if(!packet.Payload.AsSpan().SequenceEqual(Bytes(expected,"prefix_hex").Concat(Bytes(expected,"payload_hex")).ToArray()))throw new Exception("Uncompared dialog publication");
        return packet.Payload;
    }
    private static WorldCreatureInitialActor Actor(byte[] b)=>new(new(Word(b,12),Word(b,16),Word(b,20),Short(b,24),Int(b,28),Int(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60)),
        new(b.AsSpan(0x3b0,238).ToArray(),b.AsSpan(0x4d0,24).ToArray(),b[0x4ce],b[0x4cf]),Word(b,0x1f0),Word(b,0x1f4),Short(b,0x25f),Short(b,0x574),0,0,[])
    {
        Direction=new(BitConverter.UInt32BitsToSingle(Word(b,0x70)),BitConverter.UInt32BitsToSingle(Word(b,0x74)),BitConverter.UInt32BitsToSingle(Word(b,0x78))),
        ScriptInitialPosition=new(Int(b,0x2bd),Int(b,0x2c1),b[0x2c5],Short(b,0x2b9)),ScriptGroup=Word(b,0x249),RuntimeFlags=Word(b,0x200),Flags2B7=Short(b,0x2b7),DialogIndex=Word(b,0x245),PartnerReference=Word(b,0x570),KiMode=Short(b,0x25f)
    };
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
