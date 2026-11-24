using Sacred.Core.World;
using Sacred.GameServer.Data;

namespace Sacred.GameServer.Session;

/// <summary>
/// The game's sector grid for scripts (<c>server.sector_cell(id)</c>, <c>server.sector_at(x, y)</c>): sector ids are slots of the
/// native sector table, not coordinates, so the grid comes from <c>World/sectors.keyx</c> of the game directory. Loaded on first use;
/// without --game-dir (or without the file) both answer nothing.
/// </summary>
public sealed partial class GameServerHost
{
    private sealed record SectorGridData(SacredSectorIndex Index, SacredSectorGrid Grid);

    private SectorGridData? _sectorGrid;
    private bool _sectorGridTried;

    private SectorGridData? SectorGridOrNull()
    {
        if (_sectorGridTried) return _sectorGrid;
        _sectorGridTried = true;
        try
        {
            if (string.IsNullOrEmpty(_config.GameDirectory)) return null;
            var path = Path.Combine(_config.GameDirectory, "World", "sectors.keyx");
            if (!File.Exists(path)) return null;
            var index = SacredSectorIndex.Read(path);
            _sectorGrid = new SectorGridData(index, index.CreateGrid());
            Log.Info($"SCRIPT: sector grid loaded ({index.Count} sectors).");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Info($"SCRIPT: sector grid not available: {e.Message}");
        }
        return _sectorGrid;
    }

    (int X, int Y)? Scripting.IScriptServer.SectorCell(uint id)
    {
        if (SectorGridOrNull() is not { } data || !data.Index.TryGetSector(id, out var record)) return null;
        var cell = SacredSectorCoordinates.ToGrid(record);
        return (cell.X, cell.Y);
    }

    private RegionMap? _regionMap;
    private bool _regionMapTried;

    /// <summary>The game's regions and the region of every sector (sectors.keyx + bin/Balance.bin + the game texts), loaded on first use; null without --game-dir or the files.</summary>
    public RegionMap? RegionMapOrNull()
    {
        if (_regionMapTried) return _regionMap;
        _regionMapTried = true;
        try
        {
            if (string.IsNullOrEmpty(_config.GameDirectory)) return null;
            _regionMap = RegionMap.Load(_config.GameDirectory, key => Names?.TextByKey(key));
            Log.Info($"SCRIPT: {_regionMap.Regions.Count} regions, {_regionMap.RegionOfSector.Count} sectors in a region.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Info($"SCRIPT: regions not available: {e.Message}");
        }
        return _regionMap;
    }

    int Scripting.IScriptServer.SectorRegion(int sector) => sector > 0 && RegionMapOrNull() is { } map ? map.RegionAt((uint)sector) : 0;

    IReadOnlyList<Scripting.ScriptRegion> Scripting.IScriptServer.Regions() =>
        RegionMapOrNull() is { } map
            ? map.Regions.Select(r => new Scripting.ScriptRegion(r.Id, r.Key, r.Name, map.SectorsOf.TryGetValue(r.Id, out var s) ? s.Length : 0, r.KillsNeeded, r.Boss)).ToArray()
            : [];

    IReadOnlyList<uint> Scripting.IScriptServer.RegionSectors(int region) =>
        RegionMapOrNull() is { } map && map.SectorsOf.TryGetValue(region, out var sectors) ? sectors : [];

    int Scripting.IScriptServer.SectorOwner(int sector) => sector is > 0 and < SectorTableSize ? _sectorOwner[sector] : 0;

    uint Scripting.IScriptServer.SectorAt(int cellX, int cellY) =>
        SectorGridOrNull() is { } data && cellX is >= 0 and < SacredSectorCoordinates.GridWidth && cellY is >= 0 and < SacredSectorCoordinates.GridWidth
            ? data.Grid.GetSector(cellX * Sacred.Core.World.Sector.Sector.TileCount, cellY * Sacred.Core.World.Sector.Sector.TileCount) : 0;
}
