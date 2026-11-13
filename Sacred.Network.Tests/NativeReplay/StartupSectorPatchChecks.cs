using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.World;

internal static class StartupSectorPatchChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-sector-patches.json")));
        var statics=fixture.RootElement.GetProperty("static_records_hex").EnumerateArray().Select(row=>MemoryMarshal.Read<StaticObjectRecord>(Convert.FromHexString(row.GetString()!))).ToArray();
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Startup sector patches: "+why);checks++;}
        foreach(var row in fixture.RootElement.GetProperty("samples").EnumerateArray())
        {
            var seed=row.GetProperty("seed").GetInt32();var anchor=seed%2==0?20:5;
            var planes=Enumerable.Range(0,3).Select(_=>new byte[131072]).ToArray();
            for(var level=0;level<3;level++)for(var index=0;index<4096;index++)planes[level][index*32+30]=(byte)((index*13+seed+level*7)&127);
            for(var y=10;y<12;y++)for(var x=10;x<15;x++){planes[0][(y*64+x)*32+28]=unchecked((byte)(anchor-x));planes[0][(y*64+x)*32+29]=unchecked((byte)(anchor-y));}
            BinaryPrimitives.WriteUInt32LittleEndian(planes[0].AsSpan((anchor*64+anchor)*32+4),1);
            if(row.GetProperty("anchor_indoor").GetBoolean())planes[0][(anchor*64+anchor)*32+30]|=1;else planes[0][(anchor*64+anchor)*32+30]&=254;
            var payload=new WldxSectorPayload(planes[0],[new(0,0,64,64,0,planes[1]),new(0,0,64,64,0,planes[2])]);
            var surfaces=new WorldScriptIndoorSurfaces((x,y)=>(uint)x<64&&(uint)y<64?WldxTileRecord.FromBytes(planes[0].AsSpan((y*64+x)*32)):null,_=>payload,id=>id>=1&&id<=4?statics[id-1]:null);
            var patches=Convert.FromHexString(row.GetProperty("patches_hex").GetString()!).Chunk(88).Select(bytes=>GoldSectorScriptPatch.FromPayload(bytes)).ToArray();
            var positions=Convert.FromHexString(row.GetProperty("positions_hex").GetString()!).Chunk(16).Select(bytes=>GoldSectorTriggerPosition.FromPayload(bytes)).Select(value=>(value.X,value.Y,value.Surface)).ToArray();
            var registry=new GoldMouseoverRegistry();
            for(var repetition=0;repetition<2;repetition++)
            {
                WorldScriptSectorTilePatches.Apply(0,0,patches,positions,(x,y,level,flag)=>
                {
                    if((uint)x>=64||(uint)y>=64)return;
                    (byte[] Bytes,int Offset)? target=level==0?(planes[0],(y*64+x)*32):null;
                    if(level!=0){var floor=surfaces.Child(surfaces.FindParent(x,y),level);if(floor!=0)target=surfaces.IndoorCell(x,y,floor);}
                    if(target is {} cell)cell.Bytes[cell.Offset+30]|=flag;
                },registry.Register);
                for(var level=0;level<3;level++)Check(Convert.ToHexString(SHA256.HashData(planes[level].Where((_,index)=>index%32==30).ToArray())).Equals(row.GetProperty("result").GetProperty("plane_sha256")[level].GetString(),StringComparison.OrdinalIgnoreCase),"complete native flag plane after replay");
                Check(registry.Active.SelectMany(value=>value.ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("result").GetProperty("active_hex").GetString()!)),"native active registry after replay");
            }
        }
        return checks;
    }
}
