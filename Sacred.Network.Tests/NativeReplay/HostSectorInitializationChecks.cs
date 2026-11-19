using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class HostSectorInitializationChecks
{
    public static int Run(string root)
    {
        using var selection = Read(root,"native-host-sector-initialization");
        using var effects = Read(root,"native-host-sector-init-effects");
        var sector = new WorldScriptSectorEnvironment(MemoryMarshal.Read<KeyxSectorRecord>(Bytes(effects.RootElement,"keyx_hex")));
        var checks = 0;
        foreach (var row in selection.RootElement.GetProperty("cases").EnumerateArray())
        {
            var functions = row.GetProperty("names").EnumerateArray().Select(value => new SacredScriptFunction(value.GetString()!,0,0,-1,0,0)).ToArray();
            uint? packed = row.GetProperty("cached").ValueKind == JsonValueKind.Null ? null : row.GetProperty("cached").GetUInt32();
            var context = new WorldScriptExecutionContext(new(),functions,ReadOnlyMemory<byte>.Empty,true);
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            var calls = new List<ushort>();
            var result = WorldScriptSectorInitialization.TryInvoke(context,sector,_ => packed,index =>
            {
                calls.Add(index);
                Check(context.IsInitializingSector && ReferenceEquals(context.CurrentSector,sector) &&
                    !context.IsExecutingStartup && context.AssociatedCreatureReference==0,"native ordinary/null-actor sector scope");
                return true;
            });
            var nativeCalls = row.GetProperty("events").EnumerateArray().Where(value => value[0].GetString()=="invoke").ToArray();
            Check(calls.SequenceEqual(nativeCalls.Select(value => value[1].GetUInt16())),"native low-word cache, missing and index-zero distinction");
            Check(result==row.GetProperty("result").GetBoolean(),"native result ignores preceding region and callback booleans");
            Check(!context.IsInitializingSector && context.CurrentSector is null,"native scope/global cleanup");
        }
        var first=selection.RootElement.GetProperty("first_neighbor");
        var firstIndex=first.GetProperty("events")[1][1].GetInt32();
        var firstDefinition=Bytes(selection.RootElement,"first_neighbor_definition_hex");
        var firstCode=Bytes(selection.RootElement,"first_neighbor_code_hex");
        var firstSector=new WorldScriptSectorEnvironment(MemoryMarshal.Read<KeyxSectorRecord>(Bytes(selection.RootElement,"first_neighbor_keyx_hex")));
        var firstFunctions=Enumerable.Range(0,firstIndex+1).Select(_=>new SacredScriptFunction("reserved",0,0,-1,0,0)).ToArray();
        firstFunctions[firstIndex]=new(System.Text.Encoding.Latin1.GetString(firstDefinition.AsSpan(0,64)).TrimEnd('\0'),0,firstCode.Length,-1,0,0);
        var firstContext=new WorldScriptExecutionContext(new(),firstFunctions,firstCode,true);
        new WorldScriptExecutor(firstContext).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
        Check(WorldScriptSectorInitialization.TryInvoke(firstContext,firstSector,_=>null,index=>
        { Check(index==firstIndex,"actual first received neighbor's native selector");new WorldScriptExecutor(firstContext).Invoke(index);return true; })==true,
            "first ordered neighbor callback executes through existing VM");
        using(var parameters=Read(root,"native-sector-monster-parameters"))
        {
            var nativeParameter=parameters.RootElement.GetProperty("cases").EnumerateArray().First(row=>
                row.GetProperty("command_hex").GetString()==Convert.ToHexString(firstCode).ToLowerInvariant() && row.GetProperty("environment_present").GetBoolean());
            var expected=Bytes(selection.RootElement,"first_neighbor_keyx_hex").AsSpan(0x1e9,256).ToArray();
            Bytes(nativeParameter,"after_hex").AsSpan(0xd1,6).CopyTo(expected.AsSpan(0xd1));
            Check(firstSector.ToPayload().SequenceEqual(expected),"actual neighbor environment retains every byte beyond independently native-compared bounds");
        }
        foreach (var row in effects.RootElement.GetProperty("cases").EnumerateArray().Where(value => value.GetProperty("startup").GetInt32()==0))
        foreach (var publication in row.GetProperty("publications").EnumerateArray())
        {
            var native = Bytes(publication,"payload_hex");
            var opcode = publication.GetProperty("opcode").GetUInt16();
            var entry = GoldGameEventDecoder.Decode(new(opcode,0,1,0,native));
            var message = entry is GoldWorldTriggerEvent trigger
                ? GoldWorldTriggerCodec.Create(1,trigger.Reference,(ushort)trigger.Value,(ushort)trigger.InitialState,trigger.Name!)
                : CreatePatch((GoldTriggerPatchEvent)entry);
            var used = opcode==GoldWorldTriggerCodec.CreateOpcode ? native.Length : 20;
            Check(message.Payload.AsSpan(0,used).SequenceEqual(native.AsSpan(0,used)),"every native notification field and active coordinate");
            if (opcode==GoldTriggerPatchCodec.Opcode)
                Check(message.Payload.AsSpan(used).ToArray().All(value=>value==0),"explicit unused-tail normalization only");
        }
        Check(effects.RootElement.GetProperty("unused_tail_receiver_controls").GetInt32()==3,"native raw/zero/A5 unused tails leave full map/queue/tiles identical");
        foreach(var reply in new[]{false,true})
        {
            var payload=InterestWireExample(root,reply);var expected=payload.ToArray();
            var entry=GoldGameEventDecoder.Decode(new(0x148,reply?(byte)0:(byte)1,1,0,payload));
            Array.Fill(payload,(byte)0xa5);
            var retained=entry is GoldSectorEntryEvent server ? server.Entry.ToPayload() : ((GoldSectorInterestEvent)entry).Request.ToPayload();
            Check(retained.SequenceEqual(expected),"interest/entry values own their payload; generic event Message is borrowed");
        }
        return checks;
        void Check(bool condition,string reason){if(!condition)throw new Exception("Host sector init: "+reason);checks++;}
    }
    internal static GoldMessage WireExample(string root, ushort opcode)
    {
        using var proof=Read(root,"native-host-sector-init-effects");
        var row=proof.RootElement.GetProperty("cases")[0].GetProperty("publications").EnumerateArray()
            .First(value=>value.GetProperty("opcode").GetUInt16()==opcode);
        var entry=GoldGameEventDecoder.Decode(new(opcode,0,1,0,Bytes(row,"payload_hex")));
        return entry is GoldWorldTriggerEvent trigger ?
            GoldWorldTriggerCodec.Create(1,trigger.Reference,(ushort)trigger.Value,(ushort)trigger.InitialState,trigger.Name!) : CreatePatch((GoldTriggerPatchEvent)entry);
    }
    internal static byte[] InterestWireExample(string root, bool reply)
    {
        using var proof=Read(root,"native-sector-interest");
        var request=GoldSectorInterestRequest.FromPayload(Bytes(proof.RootElement.GetProperty("cases")[0],"payload_hex"));
        if(!reply)return request.CreateMessage(1).Payload;
        // Controlled nine-slot reply using the original request neighborhood.
        // This is a contract fixture, separate from original-server instructions.
        var bytes=new byte[62];
        for(var index=0;index<9;index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4+index*2),request.GetEnteredSector(index));
            bytes[40+index]=1;bytes[49+index]=1;
        }
        return bytes;
    }
    private static GoldMessage CreatePatch(GoldTriggerPatchEvent patch)=>GoldTriggerPatchCodec.CreateNormalized(1,patch.TriggerReference,patch.Cells.Take(patch.ActiveCount).ToArray());
    internal static JsonDocument Read(string root,string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence",name+".json")));
    internal static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
}
