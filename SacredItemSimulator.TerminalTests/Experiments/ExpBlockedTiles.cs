using System.Text.Json;
using Sacred.Assets;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World;

namespace SacredItemSimulator.TerminalTests.Experiments;

public sealed class ExpBlockedTiles : IExperiment
{
    private const string DefaultGameDirectory = @"E:\SteamLibrary\steamapps\common\Sacred Gold";
    private const string FullBlockOutputFileName = "fullBlockedTiles.json";
    private const string FlyableBlockOutputFileName = "flyableBlockedTiles.json";
    private const int KeyxAbsoluteBias = 0x19;

    public void Run(SacredGameData sacredGameData)
    {
        ArgumentNullException.ThrowIfNull(sacredGameData);

        var fullBlockedTiles = new HashSet<TileCoordinate>();
        var flyableBlockedTiles = new HashSet<TileCoordinate>();

        var fullGameDirectory = Path.GetFullPath(DefaultGameDirectory);
        var worldDirectory = Path.Combine(fullGameDirectory, "World");
        var sectors = ReadSectors(Path.Combine(worldDirectory, "sectors.keyx"));

        using var wldxStream = new FileStream(
            Path.Combine(worldDirectory, "sectors.wldx"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.RandomAccess);
        using var wldxLoader = new WldxLoader(wldxStream);

        Console.WriteLine($"Scanning {sectors.Count} sectors for blocked tile coordinates...");
        foreach (var sector in sectors)
        {
            var payload = wldxLoader.LoadSector(sector.Entry.Id, sector.Entry);
            CollectBlockedCoordinates(
                payload.OutdoorTiles,
                Sector.TileCount,
                Sector.TileCount,
                sector.WorldX,
                sector.WorldY,
                fullBlockedTiles,
                flyableBlockedTiles);

            foreach (var indoorGroup in payload.IndoorGroups)
            {
                CollectBlockedCoordinates(
                    indoorGroup.Tiles,
                    indoorGroup.Width,
                    indoorGroup.Height,
                    indoorGroup.WorldX,
                    indoorGroup.WorldY,
                    fullBlockedTiles,
                    flyableBlockedTiles);
            }
        }

        var fullBlockOutputPath = Path.GetFullPath(FullBlockOutputFileName);
        var flyableBlockOutputPath = Path.GetFullPath(FlyableBlockOutputFileName);
        WriteCoordinates(fullBlockOutputPath, fullBlockedTiles);
        WriteCoordinates(flyableBlockOutputPath, flyableBlockedTiles);

        Console.WriteLine(
            $"Blocked tile scan complete: full={fullBlockedTiles.Count}, " +
            $"flyable={flyableBlockedTiles.Count}.");
        Console.WriteLine($"Full-block coordinates written to {fullBlockOutputPath}");
        Console.WriteLine($"Flyable-block coordinates written to {flyableBlockOutputPath}");
    }

    private static List<WorldSector> ReadSectors(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < KeyxSectorRecord.FileHeaderSize)
            throw new InvalidDataException("sectors.keyx is too small to contain a header.");

        var count = ReadSectorCount(data);
        var entries = new List<KeyxSectorRecord>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = KeyxSectorRecord.FileHeaderSize + index * KeyxSectorRecord.Size;
            entries.Add(KeyxSectorRecord.FromBytes(data.AsSpan(offset, KeyxSectorRecord.Size)));
        }

        var positionScale = InferPositionScale(entries);
        return entries
            .Select(entry => new WorldSector(
                entry,
                RoundToSectorOrigin((entry.RawX + KeyxAbsoluteBias) * positionScale),
                RoundToSectorOrigin((entry.RawY + KeyxAbsoluteBias) * positionScale)))
            .ToList();
    }

    private static int ReadSectorCount(byte[] data)
    {
        var maximumCount = (data.Length - KeyxSectorRecord.FileHeaderSize) / KeyxSectorRecord.Size;
        var count32 = BitConverter.ToUInt32(data, 4);
        var count16 = BitConverter.ToUInt16(data, 4);
        if (count32 <= maximumCount)
            return checked((int)count32);
        if (count16 <= maximumCount)
            return count16;

        throw new InvalidDataException(
            $"Cannot determine sectors.keyx entry count. " +
            $"count16={count16}, count32={count32}, maximum={maximumCount}.");
    }

    private static float InferPositionScale(IReadOnlyList<KeyxSectorRecord> entries)
    {
        var differences = new List<int>();
        CollectPositiveDifferences(entries.Select(static entry => entry.RawX), differences);
        CollectPositiveDifferences(entries.Select(static entry => entry.RawY), differences);
        if (differences.Count == 0)
            throw new InvalidDataException("Cannot infer KEYX absolute-position scale.");

        return Sector.TileCount / (float)differences.Min();
    }

    private static void CollectPositiveDifferences(IEnumerable<int> source, List<int> differences)
    {
        int? previous = null;
        foreach (var value in new SortedSet<int>(source))
        {
            if (previous is { } previousValue && value > previousValue)
                differences.Add(value - previousValue);
            previous = value;
        }
    }

    private static int RoundToSectorOrigin(float value) =>
        (int)MathF.Round(value / Sector.TileCount) * Sector.TileCount;

    private static void CollectBlockedCoordinates(
        byte[] tileData,
        int width,
        int height,
        int worldX,
        int worldY,
        HashSet<TileCoordinate> fullBlockedTiles,
        HashSet<TileCoordinate> flyableBlockedTiles)
    {
        var expectedLength = checked(width * height * WldxTileRecord.Size);
        if (tileData.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"A {width}x{height} WLDX tile grid contains {tileData.Length} bytes; " +
                $"expected {expectedLength}.");
        }

        for (var localY = 0; localY < height; localY++)
        for (var localX = 0; localX < width; localX++)
        {
            var offset = (localY * width + localX) * WldxTileRecord.Size;
            var tile = WldxTileRecord.FromBytes(tileData.AsSpan(offset, WldxTileRecord.Size));
            var coordinate = new TileCoordinate(worldX + localX, worldY + localY);
            switch (tile.Properties.TileFlags)
            {
                case WldxTileFlags.MovementBlockerA:
                    fullBlockedTiles.Add(coordinate);
                    break;
                case WldxTileFlags.MovementBlockerB:
                    flyableBlockedTiles.Add(coordinate);
                    break;
            }
        }
    }

    private static void WriteCoordinates(
        string path,
        IEnumerable<TileCoordinate> coordinates)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        foreach (var coordinate in coordinates
                     .OrderBy(static coordinate => coordinate.Y)
                     .ThenBy(static coordinate => coordinate.X))
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(coordinate.X);
            writer.WriteNumberValue(coordinate.Y);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private readonly record struct WorldSector(KeyxSectorRecord Entry, int WorldX, int WorldY);
    private readonly record struct TileCoordinate(int X, int Y);
}
