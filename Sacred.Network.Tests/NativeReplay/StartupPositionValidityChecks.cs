using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.World;

internal static class StartupPositionValidityChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-startup-position-validity.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("controls").EnumerateArray())
        {
            var bytes = new byte[32];
            bytes[30] = row.GetProperty("flags").GetByte(); bytes[31] = row.GetProperty("properties").GetByte();
            Check(WorldScriptPositionValidity.IsAllowed(WldxTileRecord.FromBytes(bytes), _ => throw new Exception("Unexpected chain lookup")) ==
                row.GetProperty("allowed").GetBoolean(), "native flags / exact behavior and terrain nibble");
        }
        foreach (var row in fixture.RootElement.GetProperty("chains").EnumerateArray())
        {
            var bytes = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), row.GetProperty("head").GetUInt32());
            var objects = row.GetProperty("entries").EnumerateArray().ToDictionary(value => value.GetProperty("reference").GetUInt32(), value =>
            {
                var descriptor = new byte[128]; descriptor[46] = value.GetProperty("category").GetByte();
                return new WorldScriptPositionOccupant(value.GetProperty("next").GetUInt32(), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(descriptor).IsCreature);
            });
            Check(WorldScriptPositionValidity.IsAllowed(WldxTileRecord.FromBytes(bytes), reference => objects.TryGetValue(reference, out var value) ? value : null) ==
                row.GetProperty("allowed").GetBoolean(), "native registry/category chain: " + row.GetProperty("name").GetString());
        }
        foreach (var row in fixture.RootElement.GetProperty("null_tiles").EnumerateArray())
            Check(WorldScriptPositionValidity.IsAllowed(null, _ => throw new Exception("Unexpected null-tile chain lookup")) == row.GetProperty("allowed").GetBoolean(), "native null-tile predicate");

        var temporary = Path.GetTempFileName();
        try
        {
            foreach (var row in fixture.RootElement.GetProperty("sectors").EnumerateArray())
            {
                var record = Bytes(row, "keyx_hex"); var sid = row.GetProperty("sector").GetUInt32();
                var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), sid);
                // Compact portable fixture keeps the native sector ID, padding absent records.
                Array.Resize(ref keyx, checked(256 + (int)sid * 768)); record.CopyTo(keyx, 256);
                var sectors = SacredSectorIndex.FromPayload(keyx);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    stream.Position = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(236));
                    stream.Write(Bytes(row, "compressed_hex"));
                }
                using var loader = new WldxLoader(File.OpenRead(temporary));
                var loads = 0; var callbacks = 0;
                WorldScriptPositionTiles? source = null;
                source = new(sectors, (id, entry) =>
                {
                    loads++; var payload = loader.LoadSector(id, entry);
                    Check(Convert.ToHexString(SHA256.HashData(payload.OutdoorTiles)).Equals(row.GetProperty("outdoor_sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                        "complete original tile bytes match native inflate / loaded pointer");
                    Check(entry.TileGridDescriptorCount * WldxTileGridDescriptorLayout.SerializedSize == Bytes(row,"descriptors_hex").Length,
                        "KEYX descriptor count is entries including outdoor origin");
                    var indoor = row.GetProperty("indoor").EnumerateArray().ToArray();
                    Check(payload.IndoorGroups.Count == indoor.Length, "native indoor pointer table extent");
                    for (var i = 0; i < indoor.Length; i++)
                    {
                        var descriptor = WldxTileGridDescriptorLayout.FromBytes(Bytes(indoor[i],"descriptor_hex"));
                        var group = payload.IndoorGroups[i];
                        Check(group.WorldX == descriptor.WorldX && group.WorldY == descriptor.WorldY && group.Width == descriptor.Width && group.Height == descriptor.Height && group.Kind == descriptor.Kind,
                            "native indoor descriptor / original reader order");
                        Check(Convert.ToHexString(SHA256.HashData(group.Tiles)).Equals(indoor[i].GetProperty("tiles_sha256").GetString(),StringComparison.OrdinalIgnoreCase),
                            "complete indoor tile bytes match native loaded pointers");
                    }
                    return payload.OutdoorTiles;
                }, (id, _) => { callbacks++; Check(source!.IsLoaded(id), "native sets readiness before initializer"); });
                foreach (var sample in row.GetProperty("samples").EnumerateArray())
                {
                    var tile = source.GetTile(sample.GetProperty("x").GetInt32(), sample.GetProperty("y").GetInt32());
                    Check(tile.HasValue && tile.Value == WldxTileRecord.FromBytes(Bytes(sample, "tile_hex")), "native tile query index and all bytes");
                    Check(WorldScriptPositionValidity.IsAllowed(tile, _ => throw new Exception("Unexpected authored chain")) == sample.GetProperty("allowed").GetBoolean(), "native real-world position predicate");
                }
                foreach (var sample in fixture.RootElement.GetProperty("sampling").EnumerateArray().Where(value => value.GetProperty("sector").GetUInt32() == sid))
                {
                    var definitions = new WorldScriptDefinitions();
                    definitions.DefinePosition("native", sample.GetProperty("x").GetInt32(), sample.GetProperty("y").GetInt32(), sample.GetProperty("radius").GetInt32());
                    var random = new SacredRandomSequence(sample.GetProperty("seed").GetUInt32());
                    var attempts = sample.GetProperty("attempts").EnumerateArray().ToArray(); var attempt = 0;
                    var position = definitions.ResolvePosition("native", random.Next, (x, y) =>
                    {
                        var native = attempts[attempt++];
                        Check(x == native.GetProperty("x").GetInt32() && y == native.GetProperty("y").GetInt32(), "native sampled candidate order");
                        var allowed = WorldScriptPositionValidity.IsAllowed(source.GetTile(x, y), _ => throw new Exception("Unexpected sampled chain"));
                        Check(allowed == native.GetProperty("allowed").GetBoolean(), "native sampled candidate validity");
                        return allowed;
                    });
                    Check(position.X == sample.GetProperty("selected_x").GetInt32() && position.Y == sample.GetProperty("selected_y").GetInt32(), "native retained final candidate");
                    Check(random.State == sample.GetProperty("rng_after").GetUInt32() && attempt == attempts.Length, "native sample count and shared RNG");
                }
                Check(loads == row.GetProperty("loads").GetInt32() && callbacks == 1, "native demand-load occurs once");
            }
        }
        finally { File.Delete(temporary); }
        return checks;
        void Check(bool value, string field) { if (!value) throw new Exception("Startup position validity: " + field); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
