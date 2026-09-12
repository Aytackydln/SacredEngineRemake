using System.Collections.Frozen;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Sector;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>
/// Spatial index of literal particle creations in one selected compiled script.
/// It preserves duplicate declarations because their control-flow context has not
/// yet been decoded.
/// </summary>
public sealed class WorldParticleScriptIndex
{
    private static readonly IReadOnlyList<WorldParticleScriptPlacement> NoPlacements =
        Array.Empty<WorldParticleScriptPlacement>();

    private readonly FrozenDictionary<SectorCoord, IReadOnlyList<WorldParticleScriptPlacement>> _placementsBySector;

    internal static WorldParticleScriptIndex Empty { get; } = new(
        string.Empty,
        NoPlacements,
        [],
        0,
        0,
        0,
        0);

    public string SourcePath { get; }
    public IReadOnlyList<WorldParticleScriptPlacement> Placements { get; }
    public int CommandCount { get; }
    public int CreateObjectCount { get; }
    public int UnsupportedCreateObjectCount { get; }
    public int NonParticleCreateObjectCount { get; }
    public int DecodedPlacementCount { get; }

    private WorldParticleScriptIndex(
        string sourcePath,
        IReadOnlyList<WorldParticleScriptPlacement> placements,
        Dictionary<SectorCoord, List<WorldParticleScriptPlacement>> placementsBySector,
        int commandCount,
        int createObjectCount,
        int unsupportedCreateObjectCount,
        int nonParticleCreateObjectCount)
    {
        SourcePath = sourcePath;
        Placements = placements;
        CommandCount = commandCount;
        CreateObjectCount = createObjectCount;
        UnsupportedCreateObjectCount = unsupportedCreateObjectCount;
        NonParticleCreateObjectCount = nonParticleCreateObjectCount;
        DecodedPlacementCount = placements.Count(static placement =>
            placement.Definition.Status == SacredParticleDefinitionStatus.Decoded);
        _placementsBySector = placementsBySector.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<WorldParticleScriptPlacement>)pair.Value.ToArray());
    }

    public static WorldParticleScriptIndex Load(
        string path,
        SacredParticleCatalogue? particleCatalogue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sourcePath = Path.GetFullPath(path);
        particleCatalogue ??= SacredParticleCatalogue.LoadEmbedded();

        var placements = new List<WorldParticleScriptPlacement>();
        var placementsBySector = new Dictionary<SectorCoord, List<WorldParticleScriptPlacement>>();
        var commandCount = 0;
        var createObjectCount = 0;
        var unsupportedCreateObjectCount = 0;
        var nonParticleCreateObjectCount = 0;

        foreach (var command in SacredCompiledScriptReader.Read(File.ReadAllBytes(sourcePath)))
        {
            commandCount++;
            if (command.Opcode != SacredScriptCommandHeaderLayout.CreateObjectOpcode)
                continue;

            createObjectCount++;
            if (!SacredScriptCreateObjectReader.TryRead(command, out var creation, out _))
            {
                unsupportedCreateObjectCount++;
                continue;
            }

            if (!particleCatalogue.TryGetDefinition(creation.TypeId, out var definition))
            {
                nonParticleCreateObjectCount++;
                continue;
            }

            var (worldX, worldY) = ResolveWorldPosition(creation, particleCatalogue.WorldUnitsPerTile);
            var placement = new WorldParticleScriptPlacement(
                command.FileOffset,
                creation,
                definition,
                worldX,
                worldY);
            placements.Add(placement);

            var sector = new SectorCoord(
                FloorToSector(worldX),
                FloorToSector(worldY));
            if (!placementsBySector.TryGetValue(sector, out var sectorPlacements))
            {
                sectorPlacements = [];
                placementsBySector.Add(sector, sectorPlacements);
            }

            sectorPlacements.Add(placement);
        }

        var result = new WorldParticleScriptIndex(
            sourcePath,
            placements.ToArray(),
            placementsBySector,
            commandCount,
            createObjectCount,
            unsupportedCreateObjectCount,
            nonParticleCreateObjectCount);
        Console.WriteLine(
            $"Particle script loaded: {sourcePath}; {result.CommandCount:N0} commands, " +
            $"{result.Placements.Count:N0} literal particle placements in " +
            $"{placementsBySector.Count:N0} sectors ({result.DecodedPlacementCount:N0} decoded), " +
            $"{result.UnsupportedCreateObjectCount:N0} unsupported object creations.");
        return result;
    }

    public IReadOnlyList<WorldParticleScriptPlacement> GetPlacements(SectorCoord sector) =>
        _placementsBySector.GetValueOrDefault(sector, NoPlacements);

    private static (float X, float Y) ResolveWorldPosition(
        SacredScriptCreateObject creation,
        float worldUnitsPerTile)
    {
        if (creation.WorldPosition is { } position)
            return (position.X / worldUnitsPerTile, position.Y / worldUnitsPerTile);
        if (creation.TilePosition is { } tile)
            return (tile.X, tile.Y);

        throw new InvalidDataException(
            $"Particle creation {creation.TypeId} has neither a world nor tile position.");
    }

    private static int FloorToSector(float worldCoordinate) =>
        (int)MathF.Floor(worldCoordinate / Sector.TileCount);
}
