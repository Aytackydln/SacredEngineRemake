using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
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
    public static byte[] RegionDialogWireExample(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-region-npc.json")));
        var publication=fixture.RootElement.GetProperty("cases").EnumerateArray()
            .SelectMany(row=>row.GetProperty("publications").EnumerateArray()).First(row=>row.GetProperty("opcode").GetUInt16()==0x11a);
        return Bytes(publication,"prefix_hex").Concat(Bytes(publication,"payload_hex")).ToArray();
    }

    public static int Run(string root)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-npc-see-command");using var services=Read("native-startup-merchant-services");using var constructor=Read("native-horse-constructor");
        using var associated=Read("native-startup-associated-npc");using var region=Read("native-region-npc");
        using var automatic=Read("native-startup-automatic-npc");using var automaticSelection=Read("native-script-automatic-level");
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
        var automaticImage=new byte[automaticSelection.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var chunk in automaticSelection.RootElement.GetProperty("definition_chunks").EnumerateArray())Bytes(chunk,"hex").CopyTo(automaticImage,chunk.GetProperty("offset").GetInt32());
        assets=assets with {AutomaticLevelDefinitions=SacredCreatureAutomaticLevelDefinitions.Read(automaticImage)};
        var checks=0;
        foreach(var operandCase in associated.RootElement.GetProperty("operand_cases").EnumerateArray())
        {
            var decoded=SacredScriptCreatureCreationReader.Read(new(0,Bytes(operandCase,"command_hex")),_=>throw new Exception("Unexpected symbolic position"));
            Check(decoded.ConstructionFlags==operandCase.GetProperty("construction_flags").GetUInt32()&&decoded.HasExplicitBehavior==operandCase.GetProperty("explicit").GetBoolean(),"new operand payloads, combined flags and explicit-behavior ordering match native parser");
            if(decoded.ConstructionFlags==0)continue;
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter=new WorldObjectAllocationCounter(48);var random=new SacredRandomSequence(2004);
            var world=new WorldScriptCreatureWorld(assets,references,counter,new(1,1,false,0,()=>1000),random,_=>{});
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true){CreatureWorld=world};
            var command=Bytes(operandCase,"command_hex");
            // Use the available template for this runtime guard, retaining the
            // independently compared operand stream and construction flags.
            BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(5),uint.Parse(templates[0].Name));
            var rejected=false;
            try {new WorldScriptExecutor(context).ExecuteStartup(command);}
            catch(WorldScriptExecutionFailure){rejected=true;}
            Check(rejected&&context.IsInvalidated&&world.Factory.Actors.Count==0&&counter.Value==48&&references.Available==1&&
                world.Pending?.Blueprint.Creation.ConstructionFlags==decoded.ConstructionFlags,"decoded ownership/quest modes remain guarded before actor/reference allocation");
        }
        foreach(var gate in associated.RootElement.GetProperty("gates").EnumerateArray())
        {
            var flags=gate.GetProperty("flags").GetUInt32();
            Check(((flags&Sacred.Core.GameBin.Scripts.SacredScriptCreatureAssociationFlags.QuestObjectMask)!=0)==gate.GetProperty("quest_association").GetBoolean(),"native quest-object gate across all flag bits");
            Check(((flags&Sacred.Core.GameBin.Scripts.SacredScriptCreatureAssociationFlags.Ownership)!=0)==gate.GetProperty("ownership").GetBoolean(),"native ownership gate across all flag bits");
        }
        Check((Sacred.Core.GameBin.Scripts.SacredScriptCreatureAssociationFlags.StartupServicesMask&
            (Sacred.Core.GameBin.Scripts.SacredScriptCreatureAssociationFlags.QuestObjectMask|Sacred.Core.GameBin.Scripts.SacredScriptCreatureAssociationFlags.Ownership))==0,"supported service profiles exclude both associated-creature effects");
        foreach(bool? heroPresence in new bool?[]{true,null})
        {
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter=new WorldObjectAllocationCounter(48);
            var world=new WorldScriptCreatureWorld(assets,references,counter,new(1,1,false,0,()=>1000),new SacredRandomSequence(2004),_=>{});
            var bytes=Bytes(associated.RootElement.GetProperty("cases")[0],"command_hex");WorldScriptExecutor executor=null!;var started=false;
            var context=new WorldScriptExecutionContext(new(),[new("",0,0,-1,0,0),new("create",0,bytes.Length,-1,0,0)],bytes,true)
            {
                CreatureWorld=world,HasActiveHero=heroPresence,
                BeforeCommand=_=>{if(heroPresence is null&&!started){started=true;executor.InvokeAssociatedCreature(1,17);}}
            };
            var rejected=false;
            executor=new(context);
            try{executor.ExecuteStartup(heroPresence is null?Convert.FromHexString("3e000400"):bytes);}
            catch(WorldScriptExecutionFailure){rejected=true;}
            Check(rejected&&context.IsInvalidated&&world.Factory.Actors.Count==0&&counter.Value==48&&references.Available==1&&context.AssociatedCreatureReference==0,"present/unknown hero automatic level cannot publish the absent-hero result or allocate an actor");
        }
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Concat(associated.RootElement.GetProperty("cases").EnumerateArray()).Concat(automatic.RootElement.GetProperty("cases").EnumerateArray()).Concat(region.RootElement.GetProperty("cases").EnumerateArray()))
        {
            var references=new GoldObjectReferencePool();references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(65u)));
            var counter=new WorldObjectAllocationCounter(48);var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var hasAutomatic=row.TryGetProperty("automatic_profile",out var automaticProfile);
            var runtime=new WorldScriptCreatureRuntime(1,hasAutomatic?automaticProfile.GetProperty("difficulty").GetUInt32():1,false,0,()=>1000)
            {
                AutomaticLevelInputs=hasAutomatic?sector=>
                {
                    Check(sector==1,"automatic inputs queried for actual placed actor sector");
                    return new(automaticProfile.GetProperty("hero_level").GetUInt16(),
                        automaticProfile.GetProperty("team_levels").EnumerateArray().Select(level=>level.ValueKind==JsonValueKind.Null?(ushort?)null:level.GetUInt16()),
                        automaticProfile.GetProperty("elapsed_milliseconds").GetUInt32(),automaticProfile.GetProperty("progress_byte").GetByte(),0,
                        automaticProfile.GetProperty("sector_loaded").GetBoolean(),automaticProfile.GetProperty("sector_present").GetBoolean(),
                        automaticProfile.GetProperty("sector_minimum").GetUInt16(),automaticProfile.GetProperty("sector_maximum").GetUInt16());
                }:null
            };
            var world=new WorldScriptCreatureWorld(assets,references,counter,runtime,random,_=>{});
            var caller=row.TryGetProperty("caller_reference",out var callerValue)?callerValue.GetUInt32():
                row.TryGetProperty("actor_reference",out callerValue)?callerValue.GetUInt32():0;
            var commandBytes=Bytes(row,"command_hex");
            var isRegion=row.TryGetProperty("startup",out var startupScope);
            SacredScriptFunction[] functions=caller==0&&!isRegion?[]:[new("",0,0,-1,0,0),new("create",0,commandBytes.Length,-1,0,0)];
            WorldScriptExecutor executor=null!;
            var context=new WorldScriptExecutionContext(new(),functions,caller==0&&!isRegion?ReadOnlyMemory<byte>.Empty:commandBytes,true,random.Next)
            {
                CreatureWorld=world,HasActiveHero=hasAutomatic,
                BeforeCommand=command=>
                {
                    // Controlled caller boundary while native +0x14 startup scope
                    // is active. The actual CreateNPC body runs in its function.
                    if(caller!=0&&command.Opcode==Sacred.Core.GameBin.Scripts.SacredScriptControlOpcodes.Nop)
                        executor.InvokeAssociatedCreature(1,caller);
                }
            };
            var dialog = new byte[80]; BinaryPrimitives.WriteUInt32LittleEndian(dialog,7);System.Text.Encoding.Latin1.GetBytes("heilen_global").CopyTo(dialog,4);
            BinaryPrimitives.WriteUInt32LittleEndian(dialog.AsSpan(68),11);BinaryPrimitives.WriteUInt32LittleEndian(dialog.AsSpan(72),22);
            context.Registrations.Import(new[]{Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(new byte[80]),Sacred.Core.GameBin.Scripts.SacredScriptDialog.FromPayload(dialog)});
            executor=new(context);
            var nativeLevel=BinaryPrimitives.ReadUInt16LittleEndian(Bytes(row,"actor_hex").AsSpan(0x3fe));
            if(hasAutomatic&&nativeLevel>101)
            {
                var rejected=false;
                try{executor.ExecuteStartup(caller==0?commandBytes:new byte[]{62,0,4,0});}
                catch(WorldScriptExecutionFailure error){rejected=error.Message.Contains("startup XP",StringComparison.Ordinal);}
                Check(rejected&&context.IsInvalidated&&!context.StartupCompleted&&world.Prepared.Count==0&&context.AssociatedCreatureReference==0,
                    "higher native XP control stays guarded without prepared publication");
                continue;
            }
            if(isRegion&&startupScope.GetInt32()==0)
            {
                executor.ExecuteStartup(ReadOnlyMemory<byte>.Empty);
                Check(WorldScriptRegionCallbackProfile.TryInvoke(context,1,default,out var blocker),"normal region factory profile: "+blocker);
                Check(!context.IsExecutingStartup&&!context.IsInitializingRegion,"normal region scope restored without startup bypass");
            }
            else executor.ExecuteStartup(caller==0?commandBytes:new byte[]{62,0,4,0});
            Check(context.StartupCompleted&&!context.IsInvalidated&&world.Pending is null,"complete native OnSee command");
            Check(context.AssociatedCreatureReference==0,"associated creature scope restored after complete native command");
            var prepared=world.Prepared.Single();var actor=prepared.Actor!;var native=Bytes(row,"actor_hex");
            Check(actor.DialogIndex==Word(native,0x245) && actor.SpellSelector==native[0x2b6] && actor.SpellParameter==BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(0x261)),"native dialog/ECS actor state");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(context.Registrations.GetRecord(1).AsSpan(76))==(isRegion&&row.GetProperty("dialog").GetBoolean()?65u:0u),"native explicit dialog attachment");
            if(isRegion)Block(Enumerable.Range(0,context.Registrations.Count).SelectMany(context.Registrations.GetRecord).ToArray(),Bytes(row,"registrations_hex"),"complete normal/startup registration rows");
            Check(actor.KiState.AsSpan().SequenceEqual(native.AsSpan(0xfc,84)) &&
                BitConverter.SingleToUInt32Bits(actor.ThinkInterval)==Word(native,0x204) && BitConverter.SingleToUInt32Bits(actor.ThinkElapsed)==Word(native,0x208),"complete native KI state and tick timing");
            Block(context.ObjectCallbacks.Records.SelectMany(callback=>callback.ToPayload()).ToArray(),Bytes(row,"callbacks_hex"),"complete native callback rows");
            Block(actor.Component.Values,native.AsSpan(0x3b0,238),"complete values");Block(actor.Component.Health,native.AsSpan(0x4d0,24),"complete health");
            Check(actor.RuntimeFlags==Word(native,0x200)&&(actor.RuntimeFlags&0x20)==0&&world.Resources.ObjectBindings.Count==0,"enclosing owned-only name flag gate");
            Check(actor.Base.Flags==Word(native,20)&&actor.BehaviorFlags==Word(native,0x1f4)&&actor.RelationClass==Word(native,0x1f0),"default behavior/relation and original flags");
            Check(random.State==row.GetProperty("rng_after").GetUInt32()&&counter.Value==49&&references.Available==0&&!actor.Node.Active,"native RNG / allocation / dormancy");
            Check(prepared.Publications.Count==row.GetProperty("publications").GetArrayLength(),"normal/startup ordered publication count");
            for(var publicationIndex=0;publicationIndex<prepared.Publications.Count;publicationIndex++)
            {
                var publication=prepared.Publications[publicationIndex];var expected=row.GetProperty("publications")[publicationIndex];
                Check(publication.Opcode==expected.GetProperty("opcode").GetUInt16(),"native notification-before-snapshot opcode order");
                if(publication.Opcode==0x11a)
                {
                    Block(publication.Payload,Bytes(expected,"prefix_hex").Concat(Bytes(expected,"payload_hex")).ToArray(),"complete pre-service 0x11A notification");
                    continue;
                }
                Block(publication.Payload.AsSpan(0,24),Bytes(expected,"prefix_hex"),"publication prefix");
                var body=publication.Payload.AsSpan(24);var nativeBody=Bytes(expected,"payload_hex");
                Block(body[..109],nativeBody.AsSpan(0,109),"complete stream prefix");Block(body[121..],nativeBody.AsSpan(121),"complete stream tail");
            }
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("NPC OnSee command: "+why);checks++;}
        void Block(ReadOnlySpan<byte> actual,ReadOnlySpan<byte> expected,string why){if(!actual.SequenceEqual(expected))throw new Exception("NPC OnSee command: "+why);checks+=actual.Length;}
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
