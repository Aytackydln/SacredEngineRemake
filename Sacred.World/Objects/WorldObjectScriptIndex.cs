using System.Collections.Frozen;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Core.World.Stairs;

namespace Sacred.World.Objects;

public sealed class WorldObjectScriptIndex
{
    private static readonly IReadOnlyList<WorldObjectScriptPlacement> EmptyPlacements = Array.Empty<WorldObjectScriptPlacement>();
    private readonly FrozenDictionary<SectorCoord, IReadOnlyList<WorldObjectScriptPlacement>> _bySector;
    public static WorldObjectScriptIndex Empty { get; } = new([]);
    public int Count { get; }
    public IReadOnlyList<WorldObjectScriptPlacement> Placements { get; }

    private WorldObjectScriptIndex(IReadOnlyList<WorldObjectScriptPlacement> placements)
    {
        Placements = placements.ToArray();
        Count = Placements.Count;
        _bySector = Placements.GroupBy(p => new SectorCoord((int)MathF.Floor(p.WorldX / Sector.TileCount), (int)MathF.Floor(p.WorldY / Sector.TileCount)))
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<WorldObjectScriptPlacement>)g.ToArray());
    }

    public static WorldObjectScriptIndex Load(
        IEnumerable<WorldObjectScriptSource> sources,
        IEnumerable<ItemsPakEntry> items)
    {
        var byType = items.ToDictionary(item => item.ItemIndex);
        var placements = new List<WorldObjectScriptPlacement>();
        var sourceIndex = 0u;
        foreach (var source in sources)
        {
            var positionsByName = SacredDefPosPosition.ReadMany(File.ReadAllBytes(source.DefPosPath))
                .ToDictionary(position => position.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var command in SacredCompiledScriptReader.Read(File.ReadAllBytes(source.ScriptPath)))
            {
                if (!SacredScriptCreateObjectReader.TryReadLiteralPlacement(command, out var creation) ||
                    creation.TypeId > ushort.MaxValue || !byType.TryGetValue((ushort)creation.TypeId, out var item) ||
                    item.ModelDesc.Category is not (SacredItemCategory.Container or SacredItemCategory.Door))
                    continue;
                var position = creation.TilePosition ?? creation.WorldPosition ??
                    (creation.SymbolicTilePosition is { } name && positionsByName.TryGetValue(name, out var resolved)
                        ? new SacredScriptPosition(resolved.X, resolved.Y, resolved.Z)
                        : (SacredScriptPosition?)null);
                if (position is null)
                    continue;
                placements.Add(new WorldObjectScriptPlacement(
                    sourceIndex * 0x04000000u + (uint)command.FileOffset,
                    creation.TypeId, position.Value.X, position.Value.Y, position.Value.Z));
            }
            sourceIndex++;
        }
        var result = new WorldObjectScriptIndex(placements);
        var doorCount = placements.Count(placement =>
            byType.TryGetValue((ushort)placement.TypeId, out var item) &&
            item.ModelDesc.Category == SacredItemCategory.Door);
        Console.WriteLine(
            $"Interactive world objects loaded: {result.Count:N0} placements from {sourceIndex} scripts " +
            $"({doorCount:N0} doors).");
        return result;
    }

    public IReadOnlyList<WorldObjectScriptPlacement> GetPlacements(SectorCoord sector) => _bySector.GetValueOrDefault(sector, EmptyPlacements);
}

public readonly record struct WorldObjectScriptPlacement(uint ScriptOffset, uint TypeId, int WorldX, int WorldY, int WorldZ);

public readonly record struct WorldObjectScriptSource(string ScriptPath, string DefPosPath);
