using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostPopupChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-host-popup.json")));
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row,"before_hex");
            var basis = new GoldObjectBaseState(Word(before,12),Word(before,16),Word(before,20),Short(before,24),Int(before,28),Int(before,32),before[36],Word(before,40),Word(before,52),before[56],before[57],Word(before,60));
            var actor = new WorldCreatureInitialActor(basis,new(new byte[238],new byte[24],0,0),0,0,0,0,0,0,[])
                { DialogIndex=Word(before,0x245),RuntimeFlags=Word(before,0x200),KiState=before.AsSpan(0xfc,84).ToArray(),EiState=before.AsSpan(0x150,84).ToArray() };
            var table = new WorldScriptRegistrationTable();
            table.Import(Bytes(row,"table_before_hex").Chunk(80).Select(value=>SacredScriptDialog.FromPayload(value)));
            var result = WorldScriptEnteredPopup.Prepare(actor,table,1);
            Check(Write(result.Dialog).AsSpan().SequenceEqual(Bytes(row.GetProperty("notifications")[0],"actor_hex")),"complete hero state at explicit 11A submission BEFORE AI selection");
            Check(Write(result.Pending).AsSpan().SequenceEqual(Bytes(row,"after_hex")),"complete final hero, pending AI action, dialog and preserved bytes");
            Check(Enumerable.Range(0,table.Count).SelectMany(table.GetRecord).SequenceEqual(Bytes(row,"table_after_hex")),"all registration bytes, old detach and new attachment");
            Check(actor.KiState.AsSpan().SequenceEqual(before.AsSpan(0xfc,84)) && actor.EiState.AsSpan().SequenceEqual(before.AsSpan(0x150,84)),"old actor owns unchanged AI buffers");
            byte[] Write(WorldCreatureInitialActor value)
            {
                var bytes=before.ToArray();value.KiState.CopyTo(bytes,0xfc);value.EiState.CopyTo(bytes,0x150);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x200),value.RuntimeFlags);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x245),value.DialogIndex);return bytes;
            }
        }
        return checks;
        void Check(bool condition,string reason){if(!condition)throw new Exception("Host POPUP: "+reason);checks++;}
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int Int(byte[] bytes,int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
}
