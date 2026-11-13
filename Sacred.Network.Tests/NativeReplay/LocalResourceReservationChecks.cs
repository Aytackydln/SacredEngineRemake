using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class LocalResourceReservationChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-local-resource-reservation.json")));var checks=0;
        void Check(bool pass,string why){if(!pass)throw new Exception("Local resources: "+why);checks++;}
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var state=new GoldScriptResourceReplica();var before=Bytes(row,"before_hex");Import(state,before);
            for(uint index=0;index<row.GetProperty("occupied_count").GetInt32();index++)state.BindObject(8310+index,0);
            var result=state.ReserveLocalResource(row.GetProperty("name").GetString()!,row.GetProperty("create").GetBoolean());
            Check(result==row.GetProperty("result").GetUInt32(),"original first-free map key, existing name, sentinels or exhaustion");
            var after=Bytes(row,"after_hex");Check(state.Names.Count*72==after.Length,"native row count");
            for(var index=0;index<state.Names.Count;index++)
            {
                var actual=state.Names[index];var expected=GoldScriptNameRecord.FromPayload(after.AsSpan(index*72,72));
                Check(actual.Name==expected.Name&&actual.ResourceId==expected.ResourceId&&actual.TriggerReference==expected.TriggerReference,"native initialized fields");
                if(index*72<before.Length)Check(actual.ToPayload().AsSpan().SequenceEqual(after.AsSpan(index*72,72)),"existing bytes retained");
                else {Check(!actual.HasCompleteBytes,"new name padding explicitly unknown");try{_=actual.ToPayload();Check(false,"unknown padding serialized");}catch(InvalidDataException){checks++;}}
            }
        }
        foreach(var row in fixture.RootElement.GetProperty("invalidations").EnumerateArray())
        {
            var state=new GoldScriptResourceReplica();Import(state,Bytes(row,"before_hex"));state.InvalidateLocalName(row.GetProperty("name").GetString()!);
            Check(state.Names.SelectMany(name=>name.ToPayload()).SequenceEqual(Bytes(row,"after_hex")),"every matching alias tombstoned with untouched bytes retained");
        }
        return checks;
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static void Import(GoldScriptResourceReplica replica,byte[] bytes)
    {
        if(bytes.Length!=0)replica.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1b2,ScriptResourceChecks.Bank(0x1b2,4,bytes.Chunk(72).ToArray()).Payload));
    }
}
