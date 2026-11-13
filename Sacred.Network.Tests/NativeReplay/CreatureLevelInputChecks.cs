using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class CreatureLevelInputChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        using var fixture=Read("native-startup-level-inputs");
        using var basis=Read("native-creature-base-results"); using var initialization=Read("native-creature-result-initialization");
        using var finalization=Read("native-creature-result-finalization"); using var precision=Read("native-startup-floating-point");
        using var constructor=Read("native-creature-constructor"); using var passive=Read("native-creature-passive-skills");
        using var levels=Read("native-creature-level-selection"); using var operands=Read("native-creature-creation-operands");
        var image=new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach(var source in new[]{basis,initialization,finalization,precision,constructor})
        foreach(var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray()) Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var results=WorldCreatureScalarResultAssets.Read(image,Bytes(passive.RootElement,"balance_prefix_hex"));
        var construction=SacredCreatureConstructionDefinitions.Read(image);
        var levelBalance=CreatureLevelBalanceBinReader.Read(Bytes(levels.RootElement,"balance_prefix_hex"));
        var progression=CreatureBalanceBinReader.Read(Bytes(levels.RootElement,"balance_prefix_hex"));
        var definitions=SacredCreatureLevelDefinitions.Read(Bytes(levels.RootElement,"cap_override_code_hex"));
        var seed=fixture.RootElement.GetProperty("factory");
        var archive=new byte[256+CreatureGoldRecordLayout.Size*2]; BinaryPrimitives.WriteUInt32LittleEndian(archive,0x00464943); BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),2);
        Bytes(seed,"template_hex").CopyTo(archive,256+CreatureGoldRecordLayout.Size);
        var template=CreaturePakArchive.Parse(archive).Entries[1];
        var descriptor=new ItemsPakEntry(new((ushort)template.ItemType,0,128,0),MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(seed,"descriptor_hex")),"native");
        var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references=new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(seed.GetProperty("reference").GetUInt32())));
            var random=new SacredRandomSequence(seed.GetProperty("rng_seed").GetUInt32());
            var factory=new WorldCreatureInitialFactory(results,construction,references,new(seed.GetProperty("counter_before").GetByte()),random,seed.GetProperty("owner").GetByte(),1);
            var actor=factory.Create(template,descriptor,seed.GetProperty("clock").GetUInt32(),seed.GetProperty("scheduler_clock").GetDouble(),true,false);
            actor=WorldCreatureScriptLevel.Apply(actor,template,row.GetProperty("assigned").GetInt32(),results,levelBalance,progression,definitions,
                row.GetProperty("difficulty").GetUInt32(),1,true,0,0);
            Compare(actor,Bytes(row,"after_assigned_hex"));
            actor=WorldCreatureScriptLevel.ApplyRequested(actor,template,row.GetProperty("requested").GetInt32(),results,levelBalance,progression,definitions,
                row.GetProperty("difficulty").GetUInt32(),1,true,0,0);
            Compare(actor,Bytes(row,"after_requested_hex"));
        }
        foreach(var row in operands.RootElement.GetProperty("cases").EnumerateArray().Where(row=>row.GetProperty("name").GetString()!.StartsWith("assigned_",StringComparison.Ordinal) || row.GetProperty("name").GetString()=="requested_then_assigned"))
        {
            var creation=SacredScriptCreatureCreationReader.Read(new(0,Bytes(row,"command_hex")),_=>throw new Exception("Unexpected position."));
            var expected=row.GetProperty("result");
            if(creation.AssignedLevelInput!=expected.GetProperty("assigned_level").GetInt32() || creation.LevelInput!=expected.GetProperty("level").GetInt32() || creation.HasExplicitBehavior!=expected.GetProperty("explicit").GetBoolean())
                throw new Exception("Original assigned/requested level operands and ordering differ.");
            checks++;
        }
        return checks;
        void Compare(WorldCreatureInitialActor actor,byte[] native)
        {
            if(!actor.Component.Values.AsSpan().SequenceEqual(native.AsSpan(0x3b0,238)) || !actor.Component.Health.AsSpan().SequenceEqual(native.AsSpan(0x4d0,24)) ||
                actor.Component.ResultValid!=native[0x4cf] || actor.BehaviorFlags!=BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x1f4)))
                throw new Exception("Original early assigned/later requested level phase differs in values, health, validity or behavior.");
            checks+=238+24+2;
        }
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}
