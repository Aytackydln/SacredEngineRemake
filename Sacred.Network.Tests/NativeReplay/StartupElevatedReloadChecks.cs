using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.World;

internal static class StartupElevatedReloadChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-elevated-reload.json")));
        var data=fixture.RootElement; var entry=KeyxSectorRecord.FromBytes(Bytes(data,"keyx_hex"));
        using var stream=new ZLibStream(new MemoryStream(Bytes(data,"compressed_hex")),CompressionMode.Decompress);using var output=new MemoryStream();stream.CopyTo(output);var original=output.ToArray();
        var statics=data.GetProperty("statics").EnumerateArray().ToDictionary(row=>row.GetProperty("reference").GetUInt32(),row=>MemoryMarshal.Read<StaticObjectRecord>(Bytes(row,"hex")));
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Elevated reload: "+why);checks++;}
        foreach(var row in data.GetProperty("cases").EnumerateArray())
        {
            var outdoor=original.AsSpan(entry.TilesRelativeOffset,131072).ToArray();var groups=new List<WldxIndoorGroupPayload>();
            for(var i=1;i<entry.TileGridDescriptorCount;i++)
            {
                var d=WldxTileGridDescriptorLayout.FromBytes(original.AsSpan((int)entry.TileGridDescriptorsRelativeOffset+i*36));
                groups.Add(new(d.WorldX,d.WorldY,d.Width,d.Height,d.Kind,original.AsSpan((int)d.TilesOffset,(int)d.TilesSize).ToArray()));
            }
            var payload=new WldxSectorPayload(outdoor,groups);var ox=SacredSectorCoordinates.ToTileOrigin(entry.RawX);var oy=SacredSectorCoordinates.ToTileOrigin(entry.RawY);
            var surfaces=new WorldScriptIndoorSurfaces((x,y)=>(uint)(x-ox)<64&&(uint)(y-oy)<64?WldxTileRecord.FromBytes(outdoor.AsSpan(((y-oy)*64+x-ox)*32)):null,_=>payload,id=>statics.TryGetValue(id,out var value)?value:null);
            var before=Bytes(row,"before_hex");var after=Bytes(row,"after_hex");var actor=StartupSectorActor(before);
            var x=SacredWorldCoordinates.WorldToTile(actor.Base.X);var y=SacredWorldCoordinates.WorldToTile(actor.Base.Y);
            var floor=surfaces.Child(surfaces.FindParent(x,y),actor.Base.Surface);var cell=surfaces.IndoorCell(x,y,floor)!.Value;var tile=WldxTileRecord.FromBytes(cell.Bytes.AsSpan(cell.Offset));
            var result=WorldCreatureSectorReload.ReinsertIndoor(actor,tile,tile.WorldObjectChainHeadId,floor);var actual=before.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(20),result.Actor.Base.Flags);BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(44),result.Actor.StaticSurfaceReference);BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(48),result.Actor.NextOnTile);
            Check(actual.AsSpan().SequenceEqual(after),"complete native actor including retired-Horse flags");checks+=after.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset+8),result.TileHead);
            string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
            Check(Hash(outdoor).Equals(row.GetProperty("outdoor_sha256").GetString(),StringComparison.OrdinalIgnoreCase),"complete outdoor terrain");
            for(var i=0;i<groups.Count;i++)Check(Hash(groups[i].Tiles).Equals(row.GetProperty("indoor_sha256")[i].GetString(),StringComparison.OrdinalIgnoreCase),"complete indoor terrain");
        }
        return checks;
    }
    private static WorldCreatureInitialActor StartupSectorActor(byte[] b)=>new(new(Word(b,12),Word(b,16),Word(b,20),Short(b,24),Int(b,28),Int(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60)),new(b.AsSpan(0x3b0,238).ToArray(),b.AsSpan(0x4d0,24).ToArray(),b[0x4ce],b[0x4cf]),Word(b,0x1f0),Word(b,0x1f4),Short(b,0x25f),Short(b,0x574),0,0,[]){StaticSurfaceReference=Word(b,44),NextOnTile=Word(b,48)};
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
}
