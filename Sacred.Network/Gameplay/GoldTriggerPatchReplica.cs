using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>TriggerPatch2 cell mapping, pending position vector and live tile-mark work. Does not execute trigger callbacks.</summary>
public sealed class GoldTriggerPatchReplica
{
    private readonly Dictionary<uint, uint> _mappings = [];
    private readonly GoldTriggerPatchMap _tree = new();
    private readonly List<GoldSectorTriggerPosition> _positions = [];
    private readonly List<GoldSectorTriggerPosition> _incremental = [];
    private readonly HashSet<(int X, int Y, byte Surface)> _knownPositions = [];
    private readonly HashSet<GoldSectorTriggerPosition> _unrouted = [];
    private readonly Queue<GoldTriggerPatchCell> _tiles = [];
    private SacredSectorGrid? _grid;
    public GoldTriggerPatchReplica()
    {
        Mappings = new ReadOnlyDictionary<uint, uint>(_mappings);
        Positions = _positions.AsReadOnly(); IncrementalPositions = _incremental.AsReadOnly();
    }
    public IReadOnlyDictionary<uint, uint> Mappings { get; }
    /// <summary>Native in-order entries, retaining duplicate keys. Mappings exposes the value found by native lookup.</summary>
    public IReadOnlyList<(uint Key, uint TriggerReference)> MappingEntries => _tree.Entries.ToArray();
    public IReadOnlyList<GoldSectorTriggerPosition> Positions { get; }
    public IReadOnlyList<GoldSectorTriggerPosition> IncrementalPositions { get; }
    public int PendingRoutingCount => _unrouted.Count;
    public int PendingTileCount => _tiles.Count;
    public bool TryPeekTile(out GoldTriggerPatchCell cell) => _tiles.TryPeek(out cell);
    public void CompleteTile(GoldTriggerPatchCell cell)
    {
        if (!_tiles.TryPeek(out var head) || head != cell) throw new InvalidOperationException("Complete the current trigger-patch tile first.");
        _tiles.Dequeue();
    }
    public void ConfigureSectors(SacredSectorIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (_grid is not null) return;
        _grid = index.CreateGrid();
        var replacements = _unrouted.ToDictionary(position => position,
            position => position.WithSector(unchecked((ushort)_grid.GetSector(position.X, position.Y))));
        for (var i = 0; i < _positions.Count; i++)
            if (replacements.TryGetValue(_positions[i], out var replacement)) _positions[i] = replacement;
        for (var i = 0; i < _incremental.Count; i++)
            if (replacements.TryGetValue(_incremental[i], out var replacement)) _incremental[i] = replacement;
        _unrouted.Clear();
    }
    internal void Apply(GoldTriggerPatchEvent entry)
    {
        var update = GoldTriggerPatchCodec.Decode(entry.Message);
        try
        {
            foreach (var cell in update.Cells.Take(update.ActiveCount))
            {
                if (_tree.TryGetValue(cell.Key, out var current) && current == update.TriggerReference) return;
                _tree.Insert(cell.Key, update.TriggerReference);
                var surface = unchecked((byte)cell.Surface);
                if (!_knownPositions.Add((cell.X, cell.Y, surface))) return;
                var bytes = new byte[GoldSectorTriggerPosition.Size];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), cell.X);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), cell.Y); bytes[12] = surface;
                var position = GoldSectorTriggerPosition.FromPayload(bytes);
                if (_grid is null) _unrouted.Add(position);
                else position = position.WithSector(unchecked((ushort)_grid.GetSector(cell.X, cell.Y)));
                _positions.Add(position); _incremental.Add(position); _tiles.Enqueue(cell);
            }
        }
        finally { RefreshMappings(); }
    }
    private void RefreshMappings()
    {
        _mappings.Clear();
        foreach (var (key, _) in _tree.Entries)
            if (!_mappings.ContainsKey(key) && _tree.TryGetValue(key, out var value)) _mappings.Add(key, value);
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode == GoldWorldBankLayout.Bank3Opcode && bank.Sections[2].Count != 0)
        {
            _tree.Clear();
            foreach (var row in bank.Sections[2].Records)
                _tree.Insert(BinaryPrimitives.ReadUInt32LittleEndian(row.Data), BinaryPrimitives.ReadUInt32LittleEndian(row.Data[4..]));
            RefreshMappings();
        }
        if (bank.Opcode != GoldWorldBankLayout.Bank1Opcode || bank.Sections[2].Count == 0) return;
        _positions.Clear(); _incremental.Clear(); _knownPositions.Clear(); _unrouted.Clear();
        foreach (var row in bank.Sections[2].Records)
        {
            var position = GoldSectorTriggerPosition.FromPayload(row.Data); _positions.Add(position);
            _knownPositions.Add((position.X, position.Y, position.Surface));
        }
    }
    internal void Clear()
    {
        _tree.Clear(); _mappings.Clear(); _positions.Clear(); _incremental.Clear(); _knownPositions.Clear(); _unrouted.Clear(); _tiles.Clear(); _grid = null;
    }
}
