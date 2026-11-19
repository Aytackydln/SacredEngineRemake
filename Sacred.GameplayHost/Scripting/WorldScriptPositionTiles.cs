using Sacred.Core.World;
using System.Buffers.Binary;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x634AF0/0x634340 outdoor demand-loaded tiles. The required
/// Startup has no entered/protected sectors; the generic selection rule is compared separately.
/// The initializer owns registry re-placement and script patching (0x637AF0).
/// It runs after caching, so its own tile queries see the loaded sector.</summary>
public sealed class WorldScriptPositionTiles
{
    private readonly SacredSectorIndex _sectors;
    private readonly SacredSectorGrid _grid;
    private readonly Func<uint, KeyxSectorRecord, byte[]> _read;
    private readonly Action<uint, byte[]> _initialize;
    private readonly Dictionary<uint, byte[]> _loaded = [];
    private readonly List<uint> _retained = [];
    private bool _invalidated;
    public event Action<uint>? Evicted;
    public IReadOnlyList<uint> Retained => _retained.AsReadOnly();

    public WorldScriptPositionTiles(SacredSectorIndex sectors, Func<uint, KeyxSectorRecord, byte[]> read,
        Action<uint, byte[]> initialize)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(initialize);
        (_sectors, _read, _initialize) = (sectors, read, initialize);
        _grid = sectors.CreateGrid();
    }

    public bool IsLoaded(uint sector) => _loaded.ContainsKey(sector);

    internal void Invalidate() { _invalidated = true; _loaded.Clear(); _retained.Clear(); }

    public void SetHead(int x,int y,uint reference)
    {
        if (GetTile(x,y) is null) throw new InvalidDataException("Cannot link an NPC outside the original sector grid.");
        var id=_grid.GetSector(x,y);_sectors.TryGetSector(id,out var entry);
        var index=unchecked((ushort)((y-SacredSectorCoordinates.ToTileOrigin(entry.RawY))*64+x-SacredSectorCoordinates.ToTileOrigin(entry.RawX)));
        BinaryPrimitives.WriteUInt32LittleEndian(_loaded[id].AsSpan(index*WldxTileRecord.Size+8),reference);
    }

    public (byte[] Bytes, int Offset)? GetCell(int x, int y)
    {
        if (GetTile(x,y) is null) return null;
        var id = _grid.GetSector(x,y); _sectors.TryGetSector(id,out var entry);
        var index = unchecked((ushort)((y-SacredSectorCoordinates.ToTileOrigin(entry.RawY))*64+x-SacredSectorCoordinates.ToTileOrigin(entry.RawX)));
        return (_loaded[id],index*WldxTileRecord.Size);
    }

    public WldxTileRecord? GetTile(int x, int y)
    {
        if (_invalidated) throw new InvalidOperationException("Discard failed script-position tile state before retrying.");
        var id = _grid.GetSector(x, y);
        if (id == 0) return null;
        if (!_sectors.TryGetSector(id, out var entry))
            throw new InvalidDataException("Script-position grid references a missing original sector.");
        if (!_loaded.TryGetValue(id, out var tiles))
        {
            try
            {
                var retention = WorldScriptSectorRetention.Plan(_retained,_loaded.Keys.ToDictionary(key=>key,_=>WorldScriptSectorRetention.LoadedFlag));
                foreach (var removed in retention.Unload)
                {
                    _loaded.Remove(removed);
                    Evicted?.Invoke(removed);
                }
                _retained.Clear(); _retained.AddRange(retention.Retained);
                tiles = _read(id, entry);
                if (tiles.Length != 64 * 64 * WldxTileRecord.Size)
                    throw new InvalidDataException("Script-position sector has an invalid outdoor tile table.");
                _loaded.Add(id, tiles);
                _initialize(id, tiles);
                _retained.Add(id);
            }
            catch { _invalidated = true; throw; }
        }
        // Native 0x634C12 uses the low word of this complete tile index.
        var originX = SacredSectorCoordinates.ToTileOrigin(entry.RawX);
        var originY = SacredSectorCoordinates.ToTileOrigin(entry.RawY);
        var index = unchecked((ushort)((y - originY) * 64 + x - originX));
        if (index >= 64 * 64)
            throw new InvalidDataException("Script-position tile lies outside its resolved sector payload.");
        return WldxTileRecord.FromBytes(tiles.AsSpan(index * WldxTileRecord.Size));
    }
}
