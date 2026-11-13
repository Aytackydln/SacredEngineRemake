using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupNpcSeeCommandChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-npc-see-command");using var services=Read("native-startup-merchant-services");using var constructor=Read("native-horse-constructor");
        using var basis=Read("native-creature-base-results");using var initialization=Read("native-creature-result-initialization");
        using var finalization=Read("native-creature-result-finalization");using var precision=Read("native-startup-floating-point");using var levels=Read("native-creature-level-selection");
        var image=new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var source in new[]{constructor,basis,initialization,finalization,precision})
            foreach(var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var balance=Bytes(constructor.RootElement,"balance_hex");var templates=fixture.RootElement.GetProperty("templates").EnumerateObject().ToArray();
        var archive=new byte[256+(templates.Length+1)*CreatureGoldRecordLayout.Size];BinaryPrimitives.WriteUInt32LittleEndian(archive,0x00464943);BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),(uint)templates.Length+1);
        for(var i=0;i<templates.Length;i++)Convert.FromHexString(templates[i].Value.GetString()!).CopyTo(archive,256+(i+1)*CreatureGoldRecordLayout.Size);
        var descriptors=fixture.RootElement.GetProperty("descriptors").EnumerateObject().ToDictionary(value=>uint.Parse(value.Name),value=>
            new ItemsPakEntry(new(ushort.Parse(value.Name),0,128,0),MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Convert.FromHexString(value.Value.GetString()!)),""));
        var weapon=new byte[256];BinaryPrimitives.WriteUInt32LittleEndian(weapon,0x084e5057);
        var objects=SacredObjectTemplateArchive.FromPayload(descriptors,weapon);
        var keyx=new byte[256+768];BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),1);BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256+36),1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+60),SacredWorldCoordinates.TileCenterToWorld(44*64)-25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+64),SacredWorldCoordinates.TileCenterToWorld(50*64)-25);
        var assets=new WorldScriptCreatureAssets(objects,CreaturePakArchive.Parse(archive),WorldCreatureScalarResultAssets.Read(image,balance),SacredCreatureConstructionDefinitions.Read(image),
            CreatureLevelBalanceBinReader.Read(balance),CreatureBalanceBinReader.Read(balance),SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement,"cap_override_code_hex")),SacredSectorIndex.FromPayload(keyx))
            {Equipment=EquipmentGenerationFixture.ReadStartup(root), Services=SacredCreatureScriptServiceDefinitions.Read(
                Bytes(services.RootElement,"definition_code_hex"))};
        var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter=new WorldObjectAllocationCounter(48);var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var world=new WorldScriptCreatureWorld(assets,references,counter,new(1,1,false,0,()=>1000),random,_=>{});
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true,random.Next){CreatureWorld=world};
            var dialog = new byte[80]; BinaryPrimitives.WriteUInt32LittleEndian(dialog,7);System.Text.Encoding.Latin1.GetBytes("heilen_global").CopyTo(dialog,4);
            context.Registrations.Import(new[]{Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(new byte[80]),Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(dialog)});
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            Check(context.StartupCompleted&&!context.IsInvalidated&&world.Pending is null,"complete native OnSee command");
            var prepared=world.Prepared.Single();var actor=prepared.Actor!;var native=Bytes(row,"actor_hex");
            Check(actor.DialogIndex==Word(native,0x245) && actor.SpellSelector==native[0x2b6] && actor.SpellParameter==BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(0x261)),"native dialog/ECS actor state");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(context.Registrations.GetRecord(1).AsSpan(76))==0,"merchant does not attach an absent dialog");
            Check(actor.KiState.AsSpan().SequenceEqual(native.AsSpan(0xfc,84)) &&
                BitConverter.SingleToUInt32Bits(actor.ThinkInterval)==Word(native,0x204) && BitConverter.SingleToUInt32Bits(actor.ThinkElapsed)==Word(native,0x208),"complete native KI state and tick timing");
            Block(context.ObjectCallbacks.Records.SelectMany(callback=>callback.ToPayload()).ToArray(),Bytes(row,"callbacks_hex"),"complete native callback rows");
            Block(actor.Component.Values,native.AsSpan(0x3b0,238),"complete values");Block(actor.Component.Health,native.AsSpan(0x4d0,24),"complete health");
            Check(actor.RuntimeFlags==Word(native,0x200)&&(actor.RuntimeFlags&0x20)==0&&world.Resources.ObjectBindings.Count==0,"enclosing owned-only name flag gate");
            Check(actor.Base.Flags==Word(native,20)&&actor.BehaviorFlags==Word(native,0x1f4)&&actor.RelationClass==Word(native,0x1f0),"default behavior/relation and original flags");
            Check(random.State==row.GetProperty("rng_after").GetUInt32()&&counter.Value==49&&references.Available==0&&!actor.Node.Active,"native RNG / allocation / dormancy");
            var publication=prepared.Publications.Single();var expected=row.GetProperty("publications")[0];
            Block(publication.Payload.AsSpan(0,24),Bytes(expected,"prefix_hex"),"publication prefix");
            var body=publication.Payload.AsSpan(24);var nativeBody=Bytes(expected,"payload_hex");
            Block(body[..109],nativeBody.AsSpan(0,109),"complete stream prefix");Block(body[121..],nativeBody.AsSpan(121),"complete stream tail");
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("NPC OnSee command: "+why);checks++;}
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string why){if(!actual.SequenceEqual(expected))throw new Exception("NPC OnSee command: "+why);checks+=actual.Length;}
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
