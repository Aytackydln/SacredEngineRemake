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

internal static class StartupLevelStateChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-level-state");using var owners=Read("native-startup-owner");
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
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before=Actor(Bytes(row.GetProperty("before"),"horse"));
            var next=WorldCreatureScriptLevel.ApplyRequested(before,template,row.GetProperty("requested").GetInt32(),results,
                CreatureLevelBalanceBinReader.Read(balance),CreatureBalanceBinReader.Read(balance),SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement,"cap_override_code_hex")),1,1,true,0,0);
            next=WorldCreatureEquippedResults.Apply(next,template,results,data,new Dictionary<uint,WorldEquipmentInitialItem>(),1,1,true,0);
            next=next with {Component=next.Component with {ResultValid=1}};
            var expected=Bytes(row.GetProperty("after"),"horse");
            Block(next.Component.Values,expected.AsSpan(0x3b0,238),"complete level-only values / retained price");
            Block(next.Component.Health,expected.AsSpan(0x4d0,24),"complete level-only health");
            Check(next.Base==before.Base&&next.PartnerReference==before.PartnerReference&&next.DialogIndex==before.DialogIndex&&next.Component.ResultValid==expected[0x4cf],"preserved owner/dialog/base and native validity");
            Check(row.GetProperty("rng_before").GetUInt32()==row.GetProperty("rng_after").GetUInt32()&&row.GetProperty("publications").GetArrayLength()==0,"actual no-RNG / no-command-publication result");
            var full=WorldCreatureInitialSerialization.Write(next,data.Templates.GetItem(next.Base.ObjectType),new Dictionary<uint,WorldEquipmentInitialItem>(),data.Templates.GetItem).ToPayload();
            var native=Bytes(row.GetProperty("final_publication"),"payload_hex");
            Block(full.AsSpan(0,109),native.AsSpan(0,109),"complete current stream prefix");
            Block(full.AsSpan(121),native.AsSpan(121),"complete current stream tail");
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("Level-only state: "+why);checks++;}
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string why){if(!actual.SequenceEqual(expected))throw new Exception("Level-only state: "+why);checks+=actual.Length;}
    }
    private static WorldCreatureInitialActor Actor(byte[] b)=>new(new(Word(b,12),Word(b,16),Word(b,20),Short(b,24),Int(b,28),Int(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60)),
        new(b.AsSpan(0x3b0,238).ToArray(),b.AsSpan(0x4d0,24).ToArray(),b[0x4ce],b[0x4cf]),Word(b,0x1f0),Word(b,0x1f4),Short(b,0x25f),Short(b,0x574),0,0,[])
    {
        Direction=new(BitConverter.UInt32BitsToSingle(Word(b,0x70)),BitConverter.UInt32BitsToSingle(Word(b,0x74)),BitConverter.UInt32BitsToSingle(Word(b,0x78))),
        ScriptInitialPosition=new(Int(b,0x2bd),Int(b,0x2c1),b[0x2c5],Short(b,0x2b9)),ScriptGroup=Word(b,0x249),RuntimeFlags=Word(b,0x200),Flags2B7=Short(b,0x2b7),DialogIndex=Word(b,0x245),PartnerReference=Word(b,0x570)
    };
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
