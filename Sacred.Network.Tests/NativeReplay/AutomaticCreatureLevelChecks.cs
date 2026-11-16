using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class AutomaticCreatureLevelChecks
{
    public static int Run(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-automatic-level.json")));
        var data=native.RootElement;var image=new byte[data.GetProperty("image_size").GetInt32()];
        foreach(var chunk in data.GetProperty("definition_chunks").EnumerateArray())Bytes(chunk,"hex").CopyTo(image,chunk.GetProperty("offset").GetInt32());
        var balance=Bytes(data,"balance_hex");var levels=CreatureLevelBalanceBinReader.Read(balance);var progression=CreatureBalanceBinReader.Read(balance);
        var definitions=SacredCreatureAutomaticLevelDefinitions.Read(image);var checks=0;
        var unrelated=image.Concat(Bytes(data,"unrelated_progress_signature_hex")).ToArray();
        Check(SacredCreatureAutomaticLevelDefinitions.Read(unrelated).Tuning.ProgressAdditions.SequenceEqual(definitions.Tuning.ProgressAdditions),
            "unrelated identical progress switch does not override the NPC caller's operands");
        foreach(var row in data.GetProperty("cases").EnumerateArray())Compare(row,definitions);
        foreach(var variant in data.GetProperty("variants").EnumerateArray())
        {
            var changed=(byte[])image.Clone();
            foreach(var patch in variant.GetProperty("patches").EnumerateArray())Bytes(patch,"hex").CopyTo(changed,patch.GetProperty("offset").GetInt32());
            var altered=SacredCreatureAutomaticLevelDefinitions.Read(changed);
            foreach(var row in variant.GetProperty("cases").EnumerateArray())Compare(row,altered);
        }
        foreach(var row in data.GetProperty("statistics").EnumerateArray())
        {
            var payload=Bytes(row,"primary_hex").Concat(new byte[GoldPlayerStatisticsLayout.ExtensionSize]).ToArray();
            var stats=GoldPlayerStatisticsState.FromPayload(payload);
            Check(stats.AutomaticLevelElapsedMilliseconds/1000==row.GetProperty("seconds").GetUInt32(),"native statistics primary offset and unsigned milliseconds-to-seconds getter");
        }
        return checks;
        void Compare(JsonElement row,SacredCreatureAutomaticLevelDefinitions tuning)
        {
            if(row.GetProperty("requested").GetInt32()!=0)return;
            var hero=row.GetProperty("hero_level");
            if(hero.ValueKind==JsonValueKind.Null)
            {
                Check(row.GetProperty("calls").GetArrayLength()==0&&row.GetProperty("draws").GetArrayLength()==0,"native absent hero retains constructor level without a sector draw");return;
            }
            var team=row.GetProperty("team_levels").EnumerateArray().Select(level=>level.ValueKind==JsonValueKind.Null?(ushort?)null:level.GetUInt16());
            var progress=row.GetProperty("progress_byte");
            var inputs=new WorldCreatureAutomaticLevelInputs(hero.GetUInt16(),team,row.GetProperty("elapsed_milliseconds").GetUInt32(),
                progress.ValueKind==JsonValueKind.Null?(byte)0:progress.GetByte(),row.GetProperty("horse_attribute").GetByte(),
                row.GetProperty("sector_loaded").GetBoolean(),row.GetProperty("sector_present").GetBoolean(),row.GetProperty("sector_minimum").GetUInt16(),row.GetProperty("sector_maximum").GetUInt16());
            var draws=0;
            var selected=WorldCreatureAutomaticLevel.Select(inputs,row.GetProperty("difficulty").GetUInt32(),row.GetProperty("multiplayer").GetBoolean(),row.GetProperty("horse").GetBoolean(),
                levels,progression,tuning.Tuning,row.GetProperty("precision").GetInt32()==0x7f?SacredX87Precision.Single:SacredX87Precision.Double,
                ()=>{draws++;return row.GetProperty("random_value").GetInt32();});
            var call=row.GetProperty("calls")[0];
            Check(unchecked((uint)selected.RequestedLevel)==(call[0].GetUInt32()&0xffff),$"native request: managed {selected}, native {call}; inputs {row}");
            Check(call[1].GetUInt32()==1&&call[2].GetUInt32()==0,"automatic request skips downstream difficulty adjustment and keeps third flag zero");
            Check(draws==row.GetProperty("draws").GetArrayLength(),"native conditional RNG count for inclusive sector bounds");
        }
        void Check(bool pass,string why){if(!pass)throw new Exception("Automatic creature level: "+why);checks++;}
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    public static byte[] WireExample(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-automatic-level.json")));
        var row=native.RootElement.GetProperty("statistics")[5];
        var payload=Bytes(row,"primary_hex").Concat(new byte[GoldPlayerStatisticsLayout.ExtensionSize]).ToArray();
        if(GoldPlayerStatisticsState.FromPayload(payload).AutomaticLevelElapsedMilliseconds/1000!=row.GetProperty("seconds").GetUInt32())
            throw new Exception("Uncompared automatic-level elapsed counter.");
        return payload;
    }
}
