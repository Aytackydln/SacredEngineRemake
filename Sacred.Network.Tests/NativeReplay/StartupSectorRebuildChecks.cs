using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupSectorRebuildChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-sector-rebuild.json")));
        using var terrain = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-position-validity.json")));
        var sector = terrain.RootElement.GetProperty("sectors")[0];
        using var input = new MemoryStream(Bytes(sector,"compressed_hex"));
        using var zlib = new ZLibStream(input,CompressionMode.Decompress);
        using var output = new MemoryStream(); zlib.CopyTo(output);
        var entry = KeyxSectorRecord.FromBytes(Bytes(sector,"keyx_hex"));
        var original = output.ToArray().AsSpan(entry.TilesRelativeOffset,131072).ToArray();
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            foreach (var item in row.GetProperty("equipment").EnumerateArray())
                Check(Bytes(item,"before_hex").AsSpan().SequenceEqual(Bytes(item,"after_hex")),"held equipment remains byte-identical during native sector reload");
            var tiles = original.ToArray();
            foreach (var edit in row.GetProperty("tile_overrides").EnumerateArray()) tiles[edit[0].GetInt32()]=edit[1].GetByte();
            var membership = new WorldCreatureSectorMembership();
            foreach (var actorRow in row.GetProperty("actors").EnumerateArray())
            {
                var before = Bytes(actorRow,"before_hex"); var after = Bytes(actorRow,"after_hex");
                var actor = Actor(before); var initial = actor;
                if (actor.Base.Sector == row.GetProperty("sector").GetUInt32() && (actor.Base.Flags & 2) == 0)
                {
                    var x = SacredWorldCoordinates.WorldToTile(actor.Base.X) - SacredSectorCoordinates.ToTileOrigin(entry.RawX);
                    var y = SacredWorldCoordinates.WorldToTile(actor.Base.Y) - SacredSectorCoordinates.ToTileOrigin(entry.RawY);
                    var at = (y*64+x)*32; var tile = WldxTileRecord.FromBytes(tiles.AsSpan(at));
                    var result = WorldCreatureSectorReload.Reinsert(actor,tile,tile.WorldObjectChainHeadId);
                    actor = result.Actor;
                    BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(at+8),result.TileHead);
                    membership.RegisterUnenteredReload(actor);
                    Check(ReferenceEquals(actor.Component,initial.Component) && ReferenceEquals(actor.Node,initial.Node) && ReferenceEquals(actor.EquipmentReferences,initial.EquipmentReferences) && !actor.Node.Active,
                        "rebuild preserves components and dormant node");
                }
                // Assert complete native actor preservation outside the represented changed fields.
                var actual = before.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(20),actor.Base.Flags);
                BinaryPrimitives.WriteUInt16LittleEndian(actual.AsSpan(24),actor.Base.Sector);
                BinaryPrimitives.WriteInt32LittleEndian(actual.AsSpan(28),actor.Base.X);
                BinaryPrimitives.WriteInt32LittleEndian(actual.AsSpan(32),actor.Base.Y);
                actual[36] = actor.Base.Surface;
                BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(48),actor.NextOnTile);
                Check(actual.AsSpan().SequenceEqual(after),"complete native actor state: "+row.GetProperty("variant").GetString());
                checks += after.Length;
            }
            Check(Convert.ToHexString(SHA256.HashData(tiles)).Equals(row.GetProperty("tiles_sha256").GetString(),StringComparison.OrdinalIgnoreCase),
                "complete rebuilt original terrain and dynamic heads");
            Check(membership.Get((ushort)entry.Id).SequenceEqual(row.GetProperty("members").EnumerateArray().Select(value=>value.GetUInt32())),"native registry traversal / sector membership order");
            Check(row.GetProperty("runtime_flags").GetUInt32()==WorldScriptCreaturePlacementBranch.TileDataInitializedFlag,"loaded sector selects positioned factory");
        }
        return checks;
        void Check(bool value,string field) { if(!value) throw new Exception("Startup sector rebuild: "+field); checks++; }
    }
    private static WorldCreatureInitialActor Actor(byte[] b) => new(new(Word(b,12),Word(b,16),Word(b,20),Short(b,24),Int(b,28),Int(b,32),b[36],Word(b,40),Word(b,52),b[56],b[57],Word(b,60)),
        new(b.AsSpan(0x3b0,238).ToArray(),b.AsSpan(0x4d0,24).ToArray(),b[0x4ce],b[0x4cf]),Word(b,0x1f0),Word(b,0x1f4),Short(b,0x25f),Short(b,0x574),0,0,[])
        { NextOnTile=Word(b,48), EquipmentReferences=Enumerable.Range(0,19).Select(index=>Word(b,0x1a4+index*4)).ToArray() };
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ushort Short(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
