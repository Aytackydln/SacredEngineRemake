using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Sacred.Core.World.Stairs;

/// <summary>
/// Sacred's stairs trigger zones from treppe.bin, linked to their arrival markers from DefPos.bin.
/// Menu portals remain as unlinked zones.
/// </summary>
public sealed class SacredStairsMap
{
    private const int TreppeRecordSize = sizeof(uint) * 2;
    private const int ArrivalMarkerMaximumDistance = 12;

    private readonly FrozenDictionary<StairsCellKey, WorldStairsZone> _zonesByCell;
    private readonly FrozenDictionary<uint, WorldStairsLink> _linksByAnchor;

    private SacredStairsMap(
        WorldStairsCell[] cells,
        WorldStairsZone[] zones,
        WorldStairsLink[] links)
    {
        Cells = cells;
        Zones = zones;
        Links = links;
        var linkedAnchors = links
            .Select(static link => link.SourceZone.Anchor.ToPacked())
            .ToHashSet();
        _zonesByCell = zones
            .SelectMany(static zone => zone.Cells.Select(cell =>
                new KeyValuePair<StairsCellKey, WorldStairsZone>(StairsCellKey.From(cell.Position), zone)))
            .GroupBy(static pair => pair.Key)
            .ToFrozenDictionary(
                static group => group.Key,
                group => group
                    .OrderByDescending(pair => linkedAnchors.Contains(pair.Value.Anchor.ToPacked()))
                    .Select(static pair => pair.Value)
                    .First());
        _linksByAnchor = links.ToFrozenDictionary(
            static link => link.SourceZone.Anchor.ToPacked());
    }

    public IReadOnlyList<WorldStairsCell> Cells { get; }
    public IReadOnlyList<WorldStairsZone> Zones { get; }
    public IReadOnlyList<WorldStairsLink> Links { get; }

    public static SacredStairsMap Load(string treppePath, string defPosPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(treppePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defPosPath);
        return FromBytes(File.ReadAllBytes(treppePath), File.ReadAllBytes(defPosPath));
    }

    public static SacredStairsMap FromBytes(
        ReadOnlySpan<byte> treppeData,
        ReadOnlySpan<byte> defPosData)
    {
        var cells = ReadCells(treppeData);
        var zones = cells
            .GroupBy(static cell => cell.Anchor)
            .Select(static group => new WorldStairsZone(group.Key, group.ToArray()))
            .ToArray();
        var positions = SacredDefPosPosition.ReadMany(defPosData);
        var links = BuildLinks(zones, positions);
        return new SacredStairsMap(cells, zones, links);
    }

    public bool TryGetZone(
        float worldX,
        float worldY,
        out WorldStairsZone zone) =>
        _zonesByCell.TryGetValue(
            new StairsCellKey(
                (int)MathF.Floor(worldX),
                (int)MathF.Floor(worldY)),
            out zone!);

    public bool TryGetLink(
        float worldX,
        float worldY,
        out WorldStairsLink link)
    {
        if (TryGetZone(worldX, worldY, out var zone) &&
            _linksByAnchor.TryGetValue(zone.Anchor.ToPacked(), out link!))
        {
            return true;
        }

        link = null!;
        return false;
    }

    public IEnumerable<WorldStairsCell> EnumerateCells(
        int minimumX,
        int minimumY,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        var maximumX = checked(minimumX + width);
        var maximumY = checked(minimumY + height);
        foreach (var cell in Cells)
        {
            var position = cell.Position;
            if (position.X >= minimumX && position.X < maximumX &&
                position.Y >= minimumY && position.Y < maximumY)
            {
                yield return cell;
            }
        }
    }

    private static WorldStairsCell[] ReadCells(ReadOnlySpan<byte> data)
    {
        if (data.Length % TreppeRecordSize != 0)
            throw new InvalidDataException(
                $"treppe.bin length {data.Length} is not a multiple of its {TreppeRecordSize}-byte record size.");

        var cells = new WorldStairsCell[data.Length / TreppeRecordSize];
        for (var index = 0; index < cells.Length; index++)
        {
            var offset = index * TreppeRecordSize;
            var position = WorldStairsCoordinate.FromPacked(
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint))));
            var anchor = WorldStairsCoordinate.FromPacked(
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + sizeof(uint), sizeof(uint))));
            cells[index] = new WorldStairsCell(position, anchor);
        }

        return cells;
    }

    private static WorldStairsLink[] BuildLinks(
        WorldStairsZone[] zones,
        IReadOnlyList<SacredDefPosPosition> positions)
    {
        var links = new Dictionary<uint, WorldStairsLink>();
        var linkedZones = new HashSet<uint>();
        BuildArrivalMarkerLinks(zones, positions, links, linkedZones);
        return links.Values.ToArray();
    }

    private static void BuildArrivalMarkerLinks(
        WorldStairsZone[] zones,
        IReadOnlyList<SacredDefPosPosition> positions,
        Dictionary<uint, WorldStairsLink> links,
        HashSet<uint> linkedZones)
    {
        const string enterPrefix = "tpreinz";
        const string exitPrefix = "tprausz";
        var pairs = new Dictionary<string, ArrivalPair>(StringComparer.OrdinalIgnoreCase);
        foreach (var position in positions)
        {
            string? key = null;
            var isEnter = false;
            if (position.Name.StartsWith(enterPrefix, StringComparison.OrdinalIgnoreCase))
            {
                key = position.Name[enterPrefix.Length..];
                isEnter = true;
            }
            else if (position.Name.StartsWith(exitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                key = position.Name[exitPrefix.Length..];
            }

            if (string.IsNullOrEmpty(key))
                continue;

            pairs.TryGetValue(key, out var pair);
            pairs[key] = isEnter
                ? pair with { EnterArrival = position }
                : pair with { ExitArrival = position };
        }

        foreach (var (name, pair) in pairs.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.EnterArrival is not { } enterArrival ||
                pair.ExitArrival is not { } exitArrival ||
                !TryFindNearestZone(zones, enterArrival, ArrivalMarkerMaximumDistance, out var enterZone) ||
                !TryFindNearestZone(zones, exitArrival, ArrivalMarkerMaximumDistance, out var exitZone) ||
                enterZone == exitZone)
            {
                continue;
            }

            AddTwoWayLink(
                $"arrival:{name}",
                exitZone,
                new WorldStairsDestination(enterArrival.X, enterArrival.Y),
                enterZone,
                new WorldStairsDestination(exitArrival.X, exitArrival.Y),
                links,
                linkedZones);
        }
    }

    private static void AddTwoWayLink(
        string name,
        WorldStairsZone first,
        WorldStairsDestination firstDestination,
        WorldStairsZone second,
        WorldStairsDestination secondDestination,
        Dictionary<uint, WorldStairsLink> links,
        HashSet<uint> linkedZones)
    {
        var firstKey = first.Anchor.ToPacked();
        var secondKey = second.Anchor.ToPacked();
        if (linkedZones.Contains(firstKey) || linkedZones.Contains(secondKey))
            return;

        links.Add(firstKey, new WorldStairsLink(name, first, second, firstDestination));
        links.Add(secondKey, new WorldStairsLink(name, second, first, secondDestination));
        linkedZones.Add(firstKey);
        linkedZones.Add(secondKey);
    }

    private static bool TryFindNearestZone(
        IEnumerable<WorldStairsZone> zones,
        SacredDefPosPosition position,
        int maximumDistance,
        out WorldStairsZone zone)
    {
        zone = null!;
        var bestDistance = int.MaxValue;
        foreach (var candidate in zones)
        {
            var distance = Math.Abs(candidate.Anchor.X - position.X) +
                           Math.Abs(candidate.Anchor.Y - position.Y);
            if (distance < bestDistance)
            {
                zone = candidate;
                bestDistance = distance;
            }
        }

        return zone is not null && bestDistance <= maximumDistance;
    }

    private readonly record struct StairsCellKey(int X, int Y)
    {
        public static StairsCellKey From(WorldStairsCoordinate coordinate) =>
            new(coordinate.X, coordinate.Y);
    }

    private readonly record struct ArrivalPair(
        SacredDefPosPosition? EnterArrival,
        SacredDefPosPosition? ExitArrival);
}
