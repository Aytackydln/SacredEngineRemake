using System.Text.Json;

internal static class ElevatedBubblePrisonConsumerChecks
{
    public static int Run(string root)
    {
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-elevated-bubble-prison-consumers.json")));
        var checks=0;
        foreach(var read in source.RootElement.GetProperty("writer_control_reads").EnumerateArray())
            Check(read.GetBoolean(),"actual writer positively observes every candidate range");
        var rows=source.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        foreach(var row in rows)
        {
            var zero=rows.Single(value=>value.GetProperty("detail").GetInt32()==row.GetProperty("detail").GetInt32()&&value.GetProperty("elapsed").GetDouble()==row.GetProperty("elapsed").GetDouble()&&value.GetProperty("last_height").GetDouble()==row.GetProperty("last_height").GetDouble()&&value.GetProperty("pattern").GetString()=="zero");
            Check(row.GetProperty("read_count").GetInt32()==0&&row.GetProperty("draw_count").GetInt32()>0,"actual update/render ignores candidate spans and submits geometry");
            foreach(var key in new[]{"rng_after","effect_sha256","particle_sha256","submission_sha256","draw_count"})
                Check(row.GetProperty(key).ToString()==zero.GetProperty(key).ToString(),"native backing invariance: "+key);
        }
        using var received=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-elevated-bubble-prison-receive.json")));
        foreach(var row in received.RootElement.GetProperty("cases").EnumerateArray())
        {
            Check(row.GetProperty("payload_length").GetInt32()==523 && row.GetProperty("consumed").GetInt32()==523,"native full reader consumes complete state");
            Check(row.GetProperty("origin_hex").GetString()==row.GetProperty("retained_hex").GetString(),"native reader retains transmitted origin");
            Check(Convert.FromHexString(row.GetProperty("derived_hex").GetString()!).Length==128,"native reader retains complete derived state");
            var original=row.GetProperty("profile").GetString()=="original";
            Check(row.GetProperty("surface").GetInt32()==1 && row.GetProperty("runtime_surface").GetInt32()==(original?1:0) && row.GetProperty("floor").GetUInt32()==(original?658497u:0u),"native actual floor query resolves original static floor or lowers missing-floor surface");
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("BUBBLEPRISON consumers: "+why);checks++;}
    }
}
