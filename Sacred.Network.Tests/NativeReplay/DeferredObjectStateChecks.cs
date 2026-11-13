using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class DeferredObjectStateChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-deferred-object-state.json")));
        var checks=0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pending=new WorldScriptDeferredObjectStates();
            foreach (var command in row.GetProperty("commands_hex").EnumerateArray())
                pending.AppendMissing(SacredScriptObjectStateReader.Read(new(0,Convert.FromHexString(command.GetString()!))));
            var records=row.GetProperty("records_hex").EnumerateArray().Select(value=>Convert.FromHexString(value.GetString()!)).ToArray();
            Check(pending.Records.Count==records.Length,"native append/no-state/name-length/duplicate gates");
            for (var i=0;i<records.Length;i++)
            {
                var bytes=records[i];var nameEnd=bytes.AsSpan(0,64).IndexOf((byte)0);
                var actual=pending.Records[i];
                Check(actual.Name==Encoding.Latin1.GetString(bytes,0,nameEnd),"exact native name and stable native C-locale order");
                Check(actual.Open==BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(64)) && actual.Locked==BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(66)),"independent native signed open/lock fields");
                Check(actual.PackedState==BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(64)),"packed native state preserves unchanged sentinels");
                Check(actual.HasCompleteBytes==(nameEnd==63),"unknown name-tail provenance is retained, not zero-filled");
            }
        }
        return checks;
        void Check(bool pass,string why) { if (!pass) throw new Exception("Native deferred SetObjState: "+why);checks++; }
    }
}
