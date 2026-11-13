using System.Text.Json;

internal static class MagicPrisonConsumerChecks
{
    public static int Run(string root)
    {
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-magic-prison-consumers.json")));
        var checks=0;
        foreach(var read in source.RootElement.GetProperty("writer_control_reads").EnumerateArray())
            Check(read.GetBoolean(),"actual writer positively observes every candidate range");
        var rows=source.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        foreach(var row in rows)
        {
            var zero=rows.Single(value=>value.GetProperty("detail").GetInt32()==row.GetProperty("detail").GetInt32()&&value.GetProperty("elapsed").GetDouble()==row.GetProperty("elapsed").GetDouble()&&value.GetProperty("pattern").GetString()=="zero");
            Check(row.GetProperty("read_count").GetInt32()==0&&row.GetProperty("draw_count").GetInt32()>0,"actual update/render ignores candidate spans and submits geometry");
            foreach(var key in new[]{"rng_after","effect_sha256","particle_sha256","submission_sha256","draw_count"})
                Check(row.GetProperty(key).ToString()==zero.GetProperty(key).ToString(),"native backing invariance: "+key);
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("MAGICPRISON consumers: "+why);checks++;}
    }
}
