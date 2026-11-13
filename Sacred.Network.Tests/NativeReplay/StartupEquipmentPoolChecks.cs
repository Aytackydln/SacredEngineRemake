using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class StartupEquipmentPoolChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-equipment-pools.json")));var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true);
            if(row.GetProperty("initial_count").GetInt32()!=0)
                for(var index=0;index<255;index++)foreach(var type in new uint[]{90,91})context.EquipmentPools.Apply(Command(index,type));
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            for(var index=0;index<255;index++)
            {
                if(!context.EquipmentPools.Get(index).SequenceEqual(row.GetProperty("pools")[index].EnumerateArray().Select(value=>value.GetUInt32())))throw new Exception("Native equipment pool differs at "+index);
                checks++;
            }
        }
        return checks;
    }
    private static SacredScriptCommand Command(int index,uint type)
    {
        var bytes=new byte[14];BinaryPrimitives.WriteUInt16LittleEndian(bytes,114);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2),14);
        bytes[4]=11;BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5),index);bytes[9]=2;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10),type);return new(0,bytes);
    }
}
