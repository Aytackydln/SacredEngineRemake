using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.World;

internal static class ElevatedContainerReloadChecks
{
    public static int Run(string root)
    {
        var checks=0;
        foreach (var name in new[] { "native-startup-elevated-container.json", "native-host-pending-elevated-container.json" })
        {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence",name)));
        var data=fixture.RootElement;var entry=KeyxSectorRecord.FromBytes(Bytes(data,"keyx_hex"));
        using var stream=new ZLibStream(new MemoryStream(Bytes(data,"compressed_hex")),CompressionMode.Decompress);
        using var output=new MemoryStream();stream.CopyTo(output);var original=output.ToArray();
        var statics=data.GetProperty("statics").EnumerateArray().ToDictionary(row=>row.GetProperty("reference").GetUInt32(),row=>MemoryMarshal.Read<StaticObjectRecord>(Bytes(row,"hex")));
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var outdoor=original.AsSpan(entry.TilesRelativeOffset,131072).ToArray();var groups=new List<WldxIndoorGroupPayload>();
            for (var i=1;i<entry.TileGridDescriptorCount;i++)
            {
                var descriptor=WldxTileGridDescriptorLayout.FromBytes(original.AsSpan((int)entry.TileGridDescriptorsRelativeOffset+i*36));
                groups.Add(new(descriptor.WorldX,descriptor.WorldY,descriptor.Width,descriptor.Height,descriptor.Kind,
                    original.AsSpan((int)descriptor.TilesOffset,(int)descriptor.TilesSize).ToArray()));
            }
            var payload=new WldxSectorPayload(outdoor,groups);var ox=SacredSectorCoordinates.ToTileOrigin(entry.RawX);var oy=SacredSectorCoordinates.ToTileOrigin(entry.RawY);
            var surfaces=new WorldScriptIndoorSurfaces((x,y)=>(uint)(x-ox)<64&&(uint)(y-oy)<64?WldxTileRecord.FromBytes(outdoor.AsSpan(((y-oy)*64+x-ox)*32)):null,
                _=>payload,id=>statics.TryGetValue(id,out var record)?record:null);
            foreach (var item in row.GetProperty("objects").EnumerateArray().OrderBy(item=>Word(Bytes(item,"before_hex"),12)))
            {
                var before=Bytes(item,"before_hex");var after=Bytes(item,"after_hex");var basis=Basis(before);
                var x=SacredWorldCoordinates.WorldToTile(basis.X);var y=SacredWorldCoordinates.WorldToTile(basis.Y);
                var floor=surfaces.Child(surfaces.FindParent(x,y),basis.Surface);
                var cell=surfaces.IndoorCell(x,y,floor)!.Value;var tile=WldxTileRecord.FromBytes(cell.Bytes.AsSpan(cell.Offset));
                var links=WorldScriptElevatedObjectSectorReload.Reinsert(basis,Word(before,44),tile,tile.WorldObjectChainHeadId,floor);
                var actual=before.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(44),links.Floor);BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(48),links.NextOnTile);
                for (var i=0;i<actual.Length;i++) Check(actual[i]==after[i],$"complete Container byte {i:X}");
                Check(links.TileHead==basis.ObjectReference,"native new head identity");
                BinaryPrimitives.WriteUInt32LittleEndian(cell.Bytes.AsSpan(cell.Offset+8),links.TileHead);
            }
            Check(Hash(outdoor).Equals(row.GetProperty("outdoor_sha256").GetString(),StringComparison.OrdinalIgnoreCase),"complete outdoor terrain");
            for (var i=0;i<groups.Count;i++) Check(Hash(groups[i].Tiles).Equals(row.GetProperty("indoor_sha256")[i].GetString(),StringComparison.OrdinalIgnoreCase),"complete indoor terrain and ordered chain insertion");
        }
        }
        return checks;
        void Check(bool pass,string why) { if (!pass) throw new Exception("Native elevated Container reload: "+why);checks++; }
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static GoldObjectBaseState Basis(byte[] b)=>new(Word(b,12),Word(b,16),Word(b,20),BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(24)),(int)Word(b,28),(int)Word(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60));
}
