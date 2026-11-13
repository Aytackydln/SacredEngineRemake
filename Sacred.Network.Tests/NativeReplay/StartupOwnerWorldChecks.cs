using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupOwnerWorldChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-owner");using var ordering=Read("native-startup-owner-order");using var constructor=Read("native-horse-constructor");
        using var basis=Read("native-creature-base-results");using var initialization=Read("native-creature-result-initialization");
        using var finalization=Read("native-creature-result-finalization");using var precision=Read("native-startup-floating-point");
        using var levels=Read("native-creature-level-selection");using var services=Read("native-startup-creature-services");
        var image=new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var source in new[]{constructor,basis,initialization,finalization,precision})
            foreach(var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var balance=Bytes(fixture.RootElement,"balance_hex");var data=EquipmentGenerationFixture.ReadStartup(root);
        var archive=new byte[CreaturePakHeaderLayout.Size+CreatureGoldRecordLayout.Size*3];
        BinaryPrimitives.WriteUInt32LittleEndian(archive,0x00464943);BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),3);
        Bytes(fixture.RootElement.GetProperty("templates"),"288").CopyTo(archive,256+86);
        Bytes(fixture.RootElement.GetProperty("templates"),"555").CopyTo(archive,256+172);
        var keyx=new byte[256+768];BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256+0x24),1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x3c),SacredWorldCoordinates.TileCenterToWorld(52*64)-25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256+0x40),SacredWorldCoordinates.TileCenterToWorld(39*64)-25);
        var assets=new WorldScriptCreatureAssets(data.Templates,CreaturePakArchive.Parse(archive),WorldCreatureScalarResultAssets.Read(image,balance),
            SacredCreatureConstructionDefinitions.Read(image),CreatureLevelBalanceBinReader.Read(balance),CreatureBalanceBinReader.Read(balance),
            SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement,"cap_override_code_hex")),SacredSectorIndex.FromPayload(keyx))
        { Equipment=data,HorseOwner=SacredHorseOwnerDefinitions.Read(Bytes(fixture.RootElement,"owner_code_hex")),HorseValues=HorseValueBalanceBinReader.Read(balance),
          Services=SacredCreatureScriptServiceDefinitions.Read(Bytes(services.RootElement,"definition_code_hex")) };
        var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Concat(ordering.RootElement.GetProperty("cases").EnumerateArray()).Where(value=>!value.GetProperty("special_dialog").GetBoolean()))
        {
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(ScriptGroupChecks.Words(64,65)));
            var counter=new WorldObjectAllocationCounter(47);var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var world=new WorldScriptCreatureWorld(assets,references,counter,new(1,1,false,0,()=>1000),random,_=>{});
            var context=new WorldScriptExecutionContext(new WorldScriptDefinitions(),[],ReadOnlyMemory<byte>.Empty,true,random.Next){CreatureWorld=world};
            var horseCommand=Bytes(fixture.RootElement,"horse_command_hex");BinaryPrimitives.WriteInt32LittleEndian(horseCommand.AsSpan(horseCommand.Length-4),row.GetProperty("level").GetInt32());
            var ownerCommand=Bytes(fixture.RootElement,"command_hex");var requested=row.GetProperty("requested").GetInt32();
            if(requested!=0)
            {
                if(requested==3) ownerCommand=ownerCommand[..15].Concat(new byte[]{0x1f}).Concat(BitConverter.GetBytes(requested)).Concat(ownerCommand[15..]).ToArray();
                else
                {
                Array.Resize(ref ownerCommand,ownerCommand.Length+5);BinaryPrimitives.WriteUInt16LittleEndian(ownerCommand.AsSpan(2),(ushort)ownerCommand.Length);
                ownerCommand[^5]=0x1f;BinaryPrimitives.WriteInt32LittleEndian(ownerCommand.AsSpan(ownerCommand.Length-4),requested);
                }
                BinaryPrimitives.WriteUInt16LittleEndian(ownerCommand.AsSpan(2),(ushort)ownerCommand.Length);
            }
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(fixture.RootElement,"merchant_command_hex").Concat(horseCommand).Concat(ownerCommand).ToArray());
            if(!context.StartupCompleted||world.Prepared.Count!=2||world.StateCommands.Publications.Count!=1||references.Available!=0||counter.Value!=49||random.State!=row.GetProperty("rng_after").GetUInt32())
                throw new Exception("Composed startup owner command lifetime differs.");
            var horse=world.Factory.Actors[65];var owner=world.Factory.Actors[64];
            var expected=Bytes(row.GetProperty("after"),"horse");
            if(horse.PartnerReference!=64||owner.PartnerReference!=65||horse.DialogIndex!=160000||
                !horse.Component.Values.AsSpan().SequenceEqual(expected.AsSpan(0x3b0,238))||!horse.Component.Health.AsSpan().SequenceEqual(expected.AsSpan(0x4d0,24)))
                throw new Exception($"Composed startup owner fields differ: initial {row.GetProperty("level")}, requested {requested}; values {Convert.ToHexString(horse.Component.Values)} != {Convert.ToHexString(expected.AsSpan(0x3b0,238))}; health {Convert.ToHexString(horse.Component.Health)} != {Convert.ToHexString(expected.AsSpan(0x4d0,24))}");
            var message=world.StateCommands.Publications[0];var native=row.GetProperty("publications")[0];
            if(!message.Payload.AsSpan().SequenceEqual(Bytes(native,"prefix_hex").Concat(Bytes(native,"payload_hex")).ToArray()))
                throw new Exception("Composed startup owner publication differs.");
            var current=world.Prepared.Single(value=>value.Actor!.Base.ObjectReference==65);
            if(!ReferenceEquals(current.Actor,horse)||current.CreatureState is null||current.Publications.Count!=1)
                throw new Exception("Owner command must update current state while retaining historical CreateNPC publication.");
            checks+=12;
            if(row.GetProperty("level").GetInt32()==2 && row.GetProperty("seed").GetUInt32()==2004 && row.GetProperty("fill").GetInt32()==0 && requested==0)
            {
                using var levelFixture=Read("native-startup-level-state");
                var levelRow=levelFixture.RootElement.GetProperty("cases").EnumerateArray().Single(value=>value.GetProperty("level").GetInt32()==2&&value.GetProperty("fill").GetInt32()==0&&value.GetProperty("owned").GetBoolean()&&value.GetProperty("requested").GetInt32()==3);
                var levelContext=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true,random.Next){CreatureWorld=world};
                new WorldScriptExecutor(levelContext).ExecuteStartup(Bytes(levelRow,"command_hex"));
                var changed=world.Factory.Actors[65];var levelNative=Bytes(levelRow.GetProperty("after"),"horse");
                if(!levelContext.StartupCompleted||world.StateCommands.Publications.Count!=1||
                    !changed.Component.Values.AsSpan().SequenceEqual(levelNative.AsSpan(0x3b0,238))||!changed.Component.Health.AsSpan().SequenceEqual(levelNative.AsSpan(0x4d0,24))||
                    changed.PartnerReference!=64||changed.DialogIndex!=horse.DialogIndex)
                    throw new Exception("Composed level-only state must preserve price/owner/dialog and suppress startup publication.");
                checks+=6;
            }
            if(row.GetProperty("level").GetInt32()==1 && row.GetProperty("seed").GetUInt32()==2004 && row.GetProperty("fill").GetInt32()==0 && requested==0)
            {
                using var replacement=Read("native-startup-owner-replacement");var replacementRow=replacement.RootElement.GetProperty("cases")[0];
                references.Apply(GoldReferenceBatch.FromPayload(ScriptGroupChecks.Words(66)));
                var nextHorse=System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(horseCommand).Replace("res:17465","res:17467",StringComparison.Ordinal));
                var nextOwner=System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(ownerCommand).Replace("res:17465","res:17467",StringComparison.Ordinal));
                var nextContext=new WorldScriptExecutionContext(new WorldScriptDefinitions(),[],ReadOnlyMemory<byte>.Empty,true,random.Next){CreatureWorld=world};
                new WorldScriptExecutor(nextContext).ExecuteStartup(nextHorse.Concat(nextOwner).ToArray());
                if(world.StateCommands.Publications.Count!=3 || world.Factory.Actors[65].PartnerReference!=0 ||
                    world.Factory.Actors[64].PartnerReference!=66 || world.Factory.Actors[66].PartnerReference!=64)
                    throw new Exception("Composed owner replacement did not retain native reciprocal state.");
                for(var i=0;i<2;i++)
                {
                    var messageRow=replacementRow.GetProperty("publications")[i];var actual=world.StateCommands.Publications[i+1];
                    if(actual.Opcode!=messageRow.GetProperty("opcode").GetUInt16() || !actual.Payload.AsSpan().SequenceEqual(Bytes(messageRow,"prefix_hex").Concat(Bytes(messageRow,"payload_hex")).ToArray()))
                        throw new Exception("Composed owner replacement publication ordering differs.");
                }
                checks+=6;
            }
        }
        using var terrainFixture=Read("native-startup-position-validity");
        var terrainRow=terrainFixture.RootElement.GetProperty("sectors")[1];
        using var packed=new MemoryStream(Bytes(terrainRow,"compressed_hex"));
        using var inflate=new System.IO.Compression.ZLibStream(packed,System.IO.Compression.CompressionMode.Decompress);
        using var terrainPayload=new MemoryStream();inflate.CopyTo(terrainPayload);
        var terrainEntry=KeyxSectorRecord.FromBytes(Bytes(terrainRow,"keyx_hex"));
        var terrainBytes=terrainPayload.ToArray().AsSpan(terrainEntry.TilesRelativeOffset,131072).ToArray();
        var terrainAssets=assets with { ReadOutdoorTiles=(_,_)=>terrainBytes.ToArray() };
        var terrainReferences=new GoldObjectReferencePool();terrainReferences.Apply(GoldReferenceBatch.FromPayload(ScriptGroupChecks.Words(64,65,66)));
        var terrainCounter=new WorldObjectAllocationCounter(47);var terrainRandom=new SacredRandomSequence(2004);
        var terrainWorld=new WorldScriptCreatureWorld(terrainAssets,terrainReferences,terrainCounter,new(1,1,false,0,()=>1000),terrainRandom,_=>{});
        var triggerData=new byte[268];BinaryPrimitives.WriteUInt32LittleEndian(triggerData,0x01475254);
        var triggerRegistry=new WorldScriptTriggerRegistry(Sacred.Assets.World.SacredWorldTriggerArchive.FromPayload(triggerData),new GoldTriggerReferencePool());
        var positions=new WorldScriptDefinitions();positions.DefinePosition("sample",3388,2524,1);
        WorldScriptExecutionContext? terrainContext=null;
        terrainContext=new(positions,[],ReadOnlyMemory<byte>.Empty,true,terrainRandom.Next,(x,y)=>terrainWorld.Terrain!.IsAllowed(terrainContext!,x,y))
            { CreatureWorld=terrainWorld,WorldTriggers=triggerRegistry };
        var originalHorse=Bytes(fixture.RootElement,"horse_command_hex");
        var sampledHorse=originalHorse[..20].Concat(new byte[]{4,254,255,255,255}).Concat("sample\0"u8.ToArray()).Concat(originalHorse[33..]).ToArray();
        var sampledText=System.Text.Encoding.Latin1.GetString(sampledHorse).Replace("res:17465","res:17467",StringComparison.Ordinal);
        sampledHorse=System.Text.Encoding.Latin1.GetBytes(sampledText);BinaryPrimitives.WriteUInt16LittleEndian(sampledHorse.AsSpan(2),(ushort)sampledHorse.Length);
        try
        {
            new WorldScriptExecutor(terrainContext).ExecuteStartup(Bytes(fixture.RootElement,"merchant_command_hex").Concat(originalHorse).Concat(sampledHorse).ToArray());
            throw new Exception("Loaded-sector CreateNPC incorrectly completed.");
        }
        catch(WorldScriptExecutionFailure failure)
        {
            if(!failure.Message.Contains("positioned factory",StringComparison.Ordinal)||!terrainContext.IsInvalidated||terrainContext.ExecutedCommands!=2||
                terrainWorld.Prepared.Count!=2||terrainReferences.Available!=1||terrainCounter.Value!=49||terrainWorld.Pending is not { Stage:"decoded",Actor:null }||
                !terrainWorld.Terrain!.Tiles.IsLoaded(1)||!terrainWorld.Terrain.Membership.Get(1).SequenceEqual(new uint[]{64,65}))
                throw new Exception("Composed script sampling/load/rebuild must stop before positioned allocation: "+failure.Message);
            checks+=10;
        }
        return checks;
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}
