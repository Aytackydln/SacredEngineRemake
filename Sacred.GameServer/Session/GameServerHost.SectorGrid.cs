using Sacred.Core.World;

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

    uint Scripting.IScriptServer.SectorAt(int cellX, int cellY) =>
        SectorGridOrNull() is { } data && cellX is >= 0 and < SacredSectorCoordinates.GridWidth && cellY is >= 0 and < SacredSectorCoordinates.GridWidth
            ? data.Grid.GetSector(cellX * Sacred.Core.World.Sector.Sector.TileCount, cellY * Sacred.Core.World.Sector.Sector.TileCount) : 0;
}
