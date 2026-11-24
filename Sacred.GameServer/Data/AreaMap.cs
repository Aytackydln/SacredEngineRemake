using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World;

namespace Sacred.GameServer.Data;

/// <summary>
/// What the admin area editor draws from the game files: the sectors of <c>World/sectors.keyx</c> (id, cell of the 128 x 128 sector
/// grid, authored name) and the game's own world map (the Ancaria overview, 64 textures <c>UI_WORLDMAPX&lt;row&gt;&lt;column&gt;.TGA</c> of
/// Texture.pak) as one PNG. Sector ids are slots of the native sector table, not coordinates; the cell comes from the record's bounds
/// exactly as the game builds its grid (<see cref="SacredSectorCoordinates"/>).
/// <para>Map projection (the game's own, <c>Sacred.World.Map.WorldMapProjection</c>, Gold 0x6CB3B0): for a position in TILES
/// (world units / 53.66563) <c>mapX = ox + (tx - ty) * kx</c>, <c>mapY = oy + (tx + ty) * ky</c> in pixels of the PNG. A sector cell
/// (cx, cy) covers the tiles [64 cx, 64 cx + 64) x [64 cy, 64 cy + 64), a diamond on the map. Positions outside the image (the
/// underworld, caves) are not on the overview.</para>
/// Built once on demand, off the game loop; the result is immutable and shared.
/// </summary>
public sealed class AreaMap
{
    public const double WorldUnitsPerTile = 53.66563034057617;
    public const int TilesPerSector = 64;

    public sealed record SectorInfo(uint Id, int CellX, int CellY, string Name, int Flags);

    public required IReadOnlyList<SectorInfo> Sectors { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Png { get; init; }
    /// <summary>The map affine for tile coordinates: x = Ox + (tx - ty) * Kx, y = Oy + (tx + ty) * Ky.</summary>
    public required (double Ox, double Oy, double Kx, double Ky) Projection { get; init; }

    /// <summary>The map pixel of a tile position (the same formula the page uses).</summary>
    public static (double X, double Y) ToMap((double Ox, double Oy, double Kx, double Ky) p, double tileX, double tileY) =>
        (p.Ox + (tileX - tileY) * p.Kx, p.Oy + (tileX + tileY) * p.Ky);

    /// <summary>The projection for an atlas of this width: Gold's calibration is for 2048 pixels; Ancaria sits 768 rows below the combined map.</summary>
    public static (double Ox, double Oy, double Kx, double Ky) ProjectionFor(int mapWidth)
    {
        var scale = mapWidth / 2048.0;
        return (1018.7499389648438 * scale, (-186.9473876953125 - 768) * scale, 48 * 0.007446 * scale, 24 * 0.014914 * scale);
    }

    /// <summary>Reads the sector table and the world map of a game directory. Throws on missing or unreadable files.</summary>
    public static AreaMap Load(string gameDirectory)
    {
        var sectors = ReadSectors(Path.Combine(gameDirectory, "World", "sectors.keyx"));
        var (width, height, rgba) = ReadWorldMap(gameDirectory);
        return new AreaMap { Sectors = sectors, Width = width, Height = height, Png = EncodePng(width, height, rgba), Projection = ProjectionFor(width) };
    }

    public static List<SectorInfo> ReadSectors(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = (int)MemoryMarshal.Read<KeyxHeaderLayout>(bytes).SectorCount;
        if (count < 0 || count > (bytes.Length - KeyxSectorRecord.FileHeaderSize) / KeyxSectorRecord.Size)
            throw new InvalidDataException("sectors.keyx has a truncated record array");
        var list = new List<SectorInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var at = KeyxSectorRecord.FileHeaderSize + i * KeyxSectorRecord.Size;
            var record = KeyxSectorRecord.FromBytes(bytes.AsSpan(at, KeyxSectorRecord.Size));
            if (record.Id == 0) continue;
            var cell = SacredSectorCoordinates.ToGrid(record);
            var field = bytes.AsSpan(at, 32);
            var end = field.IndexOf((byte)0);
            var name = Encoding.Latin1.GetString(end >= 0 ? field[..end] : field).Trim();
            list.Add(new SectorInfo(record.Id, cell.X, cell.Y, CleanName(name), (int)record.EnvironmentFlags));
        }
        return list;
    }

    private static string CleanName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name) sb.Append(char.IsControl(ch) ? ' ' : ch);
        return sb.ToString().Trim();
    }

    private static (int Width, int Height, byte[] Rgba) ReadWorldMap(string gameDirectory)
    {
        const int columns = 8, rows = 8;
        using var textures = TexturePakArchive.LoadFromDirectory(Path.Combine(gameDirectory, "PAK"));
        var tiles = new TextureAsset[rows * columns];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                tiles[row * columns + column] = textures.LoadTextureAsync($"UI_WORLDMAPX{row}{column}.TGA").GetAwaiter().GetResult();
        var tileW = tiles[0].Width; var tileH = tiles[0].Height;
        var width = tileW * columns; var height = tileH * rows;
        var rgba = new byte[checked(width * height * 4)];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var tile = tiles[row * columns + column];
                if (tile.Width != tileW || tile.Height != tileH || tile.Rgba8.Length != tileW * tileH * 4)
                    throw new InvalidDataException($"world map tile {tile.Name} has an unexpected size");
                for (var y = 0; y < tileH; y++)
                    Buffer.BlockCopy(tile.Rgba8, y * tileW * 4, rgba, ((row * tileH + y) * width + column * tileW) * 4, tileW * 4);
            }
        return (width, height, rgba);
    }

    /// <summary>A PNG (8 bit RGB, no interlace) of RGBA pixels: no image library needed (Native AOT clean), zlib from the BCL.</summary>
    public static byte[] EncodePng(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var raw = new MemoryStream(height * (width * 3 + 1));
        var row = new byte[width * 3 + 1];
        for (var y = 0; y < height; y++)
        {
            row[0] = 0;                                    // filter: none
            for (var x = 0; x < width; x++)
            {
                var src = (y * width + x) * 4;
                row[1 + x * 3] = rgba[src]; row[2 + x * 3] = rgba[src + 1]; row[3 + x * 3] = rgba[src + 2];
            }
            raw.Write(row);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(z);
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 2;                      // 8 bit, truecolor
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body.AsSpan(4));
        stream.Write(body);
        Span<byte> crc = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        stream.Write(crc);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>CRC-32 as PNG chunks use it.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
