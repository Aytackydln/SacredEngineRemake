using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.World;

internal static class SectorZeroNpcChecks
{
    public static int Run(string root)=>Run(root,null);
    private static int Run(string root,Action<byte[]>? collect)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-sector-zero-npc");
        using var constructor=Read("native-creature-constructor");using var horse=Read("native-horse-constructor");using var basis=Read("native-creature-base-results");
        using var initialization=Read("native-creature-result-initialization");using var finalization=Read("native-creature-result-finalization");
        using var precision=Read("native-startup-floating-point");using var levels=Read("native-creature-level-selection");
        var image=new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var source in new[]{constructor,horse,basis,initialization,finalization,precision})
            foreach(var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var balance=Bytes(horse.RootElement,"balance_hex");
        var archive=new byte[256+CreatureGoldRecordLayout.Size*2];
        BinaryPrimitives.WriteUInt32LittleEndian(archive,0x00464943);BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),2);
        Bytes(fixture.RootElement,"template_hex").CopyTo(archive,256+CreatureGoldRecordLayout.Size);
        var creatures=CreaturePakArchive.Parse(archive);var type=creatures.Entries[1].ItemType;
        var descriptor=new ItemsPakEntry(new((ushort)type,0,128,0),MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(fixture.RootElement,"descriptor_hex")),"");
        var weapon=new byte[256];BinaryPrimitives.WriteUInt32LittleEndian(weapon,0x084e5057);
        var objects=SacredObjectTemplateArchive.FromPayload(new Dictionary<uint,ItemsPakEntry>{{type,descriptor}},weapon);
        var assets=new WorldScriptCreatureAssets(objects,creatures,WorldCreatureScalarResultAssets.Read(image,balance),SacredCreatureConstructionDefinitions.Read(image),
            CreatureLevelBalanceBinReader.Read(balance),CreatureBalanceBinReader.Read(balance),SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement,"cap_override_code_hex")),SacredSectorIndex.FromPayload(new byte[256]))
        {
            Equipment=EquipmentGenerationFixture.ReadStartup(root)
        };
        var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter=new WorldObjectAllocationCounter(48);var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var world=new WorldScriptCreatureWorld(assets,references,counter,new(1,1,false,0,()=>1000),random,_=>{});
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true,random.Next) { CreatureWorld=world };
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            Check(context.StartupCompleted && !context.IsInvalidated && world.Pending is null,"complete sector-zero literal command");
            var prepared=world.Prepared.Single();var actor=prepared.Actor!;var native=Bytes(row,"actor_hex");
            Block(actor.Component.Values,native.AsSpan(0x3b0,238),"complete native values");
            Block(actor.Component.Health,native.AsSpan(0x4d0,24),"complete native health");
            Check(actor.Base.Sector==Short(native,24)&&actor.Base.X==Int(native,28)&&actor.Base.Y==Int(native,32)&&actor.Base.Surface==native[36],
                $"placed indoor position seed {row.GetProperty("seed")}, fill {row.GetProperty("fill")}: {actor.Base.Sector}/{actor.Base.X}/{actor.Base.Y}/{actor.Base.Surface} vs {Short(native,24)}/{Int(native,28)}/{Int(native,32)}/{native[36]}");
            Check(actor.StaticSurfaceReference==Word(native,44)&&actor.Base.TriggerReference==Word(native,52)&&actor.NextOnTile==Word(native,48),"static floor / trigger / dynamic link separation");
            Check(actor.Base.Flags==Word(native,20)&&actor.Component.ResultValid==native[0x4cf]&&!actor.Node.Active,"complete placement flags and deactivation");
            Check(random.State==row.GetProperty("rng_after").GetUInt32()&&counter.Value==49&&references.Available==0,"shared RNG and one reservation/counter increment");
            Check(actor.Base.Sector==0 && world.Terrain is null,"native sector miss retains direct dormant placement without terrain");
            var publication=prepared.Publications.Single();collect?.Invoke(publication.Payload);var expected=row.GetProperty("publications")[0];
            Block(publication.Payload.AsSpan(0,24),Bytes(expected,"prefix_hex"),"native publication arguments");
            var body=publication.Payload.AsSpan(24);var nativeBody=Bytes(expected,"payload_hex");
            Block(body[..109],nativeBody.AsSpan(0,109),"complete creature stream prefix");
            Block(body[121..],nativeBody.AsSpan(121),"complete creature stream tail including indoor script pose");
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("Sector-zero NPC: "+why);checks++;}
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string why){if(!actual.SequenceEqual(expected))throw new Exception("Sector-zero NPC: "+why+"\n"+Convert.ToHexString(actual)+"\n"+Convert.ToHexString(expected));checks+=actual.Length;}
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<byte[]>> Wire = new();
    public static byte[] WireExample(string root)=>Wire.GetOrAdd(root,path=>new(()=>BuildWire(path))).Value.ToArray();
    private static byte[] BuildWire(string root)
    {
        byte[]? bytes=null;Run(root,payload=>bytes ??= payload);return bytes!;
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
