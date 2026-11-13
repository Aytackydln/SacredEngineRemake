using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupNpcAiChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-npc-ai.json")));var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("NPC AI: "+why);checks++;}
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before=Bytes(row,"before_hex");var expected=Bytes(row,"after_hex");
            var actor=new WorldCreatureInitialActor(default,new(new byte[238],new byte[24],0,0),Word(before,0x1f0),0,0,0,
                Float(before,0x208),0,[]){RuntimeFlags=Word(before,0x200),KiState=before.AsSpan(0xfc,84).ToArray(),ThinkInterval=Float(before,0x204),Flags2B7=Half(before,0x2b7)};
            var actual=WorldCreatureScriptControl.Apply(actor,row.GetProperty("behavior").GetUInt32(),-1,row.GetProperty("flag_input").GetInt32());
            actual.KiState.CopyTo(before,0xfc);Put(before,0x204,BitConverter.SingleToUInt32Bits(actual.ThinkInterval));Put(before,0x208,BitConverter.SingleToUInt32Bits(actual.ThinkElapsed));
            Put(before,0x1f4,actual.BehaviorFlags);BinaryPrimitives.WriteUInt16LittleEndian(before.AsSpan(0x2b7),actual.Flags2B7);
            Check(before.AsSpan().SequenceEqual(expected),"complete native AI/caller write and preservation boundary");
            Check(ReferenceEquals(actor.Component,actual.Component)&&actor.Node==actual.Node&&actual.RelationClass==actor.RelationClass&&actual.RuntimeFlags==actor.RuntimeFlags,"constructor/scheduler/relation lifetime");
        }
        foreach(var row in fixture.RootElement.GetProperty("appends").EnumerateArray())
        {
            var table=new WorldScriptObjectCallbacks();foreach(var bytes in Bytes(row,"before_hex").Chunk(68))table.Append(GoldObjectScriptCallback.FromPayload(bytes));
            table.RegisterCreature(77,row.GetProperty("name").GetString());
            Check(table.Records.SelectMany(record=>record.ToPayload()).SequenceEqual(Bytes(row,"after_hex")),"native callback order, duplicate append, empty suppression and all bytes");
        }
        return checks;
    }
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Half(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static float Float(byte[] b,int at)=>BitConverter.UInt32BitsToSingle(Word(b,at));
    private static void Put(byte[] b,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at),value);
}
