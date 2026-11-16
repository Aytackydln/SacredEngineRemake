using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class AdmissionFinalizerChecks
{
    public static int Run(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-admission-finalizer.json")));
        var checks=0;
        foreach(var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pending=Enumerable.Range(0,row.GetProperty("count").GetInt32()).ToList();
            uint word30=0xdeadbeef,word34=uint.MaxValue;var events=new List<string>();
            var result=WorldAdmissionFinalizer.Reset(row.GetProperty("present").GetBoolean(),pending,
                ()=>{word30=word34=0;},index=>
                {
                    Check(word30==0&&word34==0,"word reset before record destruction");events.Add("destroy:"+index);
                },()=>{Check(pending.Count==0,"logical clear before pump/query");events.Add("pump-query");return row.GetProperty("query").GetBoolean();});
            Check(result==(row.GetProperty("result").GetUInt32()!=0),"native query return including absent connection");
            Check(word30==row.GetProperty("word30").GetUInt32()&&word34==row.GetProperty("word34").GetUInt32(),"controller word writes");
            Check(pending.Count==row.GetProperty("count_after").GetInt32(),"pending extent cleared only with connection");
            Check(events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value=>value[0].GetString()=="destroy"?"destroy:"+value[1].GetInt32():"pump-query")),"ordered teardown and query services");
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Admission finalizer: "+why);checks++;}
    }
}
