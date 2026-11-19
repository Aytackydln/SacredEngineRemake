using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Networking;

internal static class HostAmbientScanChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-ambient-scan");
        var source = proof.RootElement;
        var definitions = SacredAmbientPopulationDefinitions.Read(Bytes(source, "definition_hex"));
        var environment = Bytes(source, "environment_hex");
        var tiles = source.GetProperty("sources").EnumerateArray().ToDictionary(row => (row.GetProperty("x").GetInt32(), row.GetProperty("y").GetInt32()), row =>
        {
            using var input = new MemoryStream(Bytes(row, "compressed_hex")); using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(); zlib.CopyTo(output);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(Bytes(row, "keyx_hex").AsSpan(0xd4));
            return output.ToArray().AsSpan(offset, 131072).ToArray();
        });
        var checks = 0;
        foreach (var row in source.GetProperty("cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32()); var created = new List<(uint Type, int X, int Y, ushort Tail, byte[] Direction)>();
            new WorldAmbientPopulationGenerator(definitions, random, Tile, (_, _) => false, (type, x, y) =>
            {
                var tail = (ushort)(random.Next() % 6); _ = random.Next();
                var dy = (float)((random.Next() % 101 - 50) * (double)0.02f); var dx = (float)((random.Next() % 101 - 50) * (double)0.02f);
                var direction = BitConverter.GetBytes(dx).Concat(BitConverter.GetBytes(dy)).Concat(new byte[4]).ToArray();
                created.Add((type, World(x), World(y), tail, direction));
                int World(int value) => row.GetProperty("control_word").GetInt32() == 0x7f ? SacredWorldCoordinates.TileCenterToWorldStartup(value) : SacredWorldCoordinates.TileCenterToWorld(value);
            }).Execute(new(51, 38), environment[0xd7], environment[0xf7], environment[0xf8]);
            var native = row.GetProperty("objects").EnumerateArray().ToArray();
            Check(created.Count == native.Length && random.State == row.GetProperty("state").GetUInt32(), "full native scan count/interleaved RNG");
            for (var index = 0; index < native.Length; index++)
                Check(created[index].Type == native[index].GetProperty("type").GetUInt32() && created[index].X == native[index].GetProperty("x").GetInt32() &&
                    created[index].Y == native[index].GetProperty("y").GetInt32() && created[index].Tail == native[index].GetProperty("tail").GetUInt16() &&
                    created[index].Direction.SequenceEqual(Bytes(native[index], "direction_hex")), "every native resource, position, tail and orientation in order");
        }
        return checks;
        SacredAmbientTile? Tile(int x, int y)
        {
            if (!tiles.TryGetValue((x / 64, y / 64), out var data)) return null;
            var value = WldxTileRecord.FromBytes(data.AsSpan(((y % 64) * 64 + x % 64) * 32));
            return new(value.Properties, new(value.VisualElevationLeft, value.VisualElevationTop, value.VisualElevationRight, value.VisualElevationBottom));
        }
        void Check(bool value, string reason) { if (!value) throw new Exception("Host ambient scan: " + reason); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string name) => HostSectorInitializationChecks.Bytes(row, name);
}
