using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Ordered native definitions. DefPos replaces the first matching position;
/// DefPoolPos appends, including repeated names. Pools are never collapsed into a dictionary.</summary>
public sealed class WorldScriptDefinitions
{
    private readonly List<WorldScriptDefinition> _records = [];
    public WorldScriptDefinitions() => Records = _records.AsReadOnly();
    public IReadOnlyList<WorldScriptDefinition> Records { get; }

    public void Import(IEnumerable<WorldScriptDefinition> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        _records.Clear();
        _records.AddRange(copy);
    }

    public static WorldScriptDefinitions Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) throw new InvalidDataException("Truncated DefPos.bin header.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var start = 4;
        if (count == SacredDefPosVersionedHeaderLayout.FormatMarker)
        {
            start = SacredDefPosVersionedHeaderLayout.SerializedSize;
            if (data.Length < start) throw new InvalidDataException("Truncated versioned DefPos.bin header.");
            count = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        }
        if (count > (data.Length - start) / SacredDefPosPositionLayout.SerializedSize)
            throw new InvalidDataException("Truncated DefPos.bin definition table.");
        var result = new WorldScriptDefinitions();
        for (var i = 0; i < count; i++)
            result._records.Add(WorldScriptDefinition.FromPayload(data.Slice(start + i * SacredDefPosPositionLayout.SerializedSize, SacredDefPosPositionLayout.SerializedSize)));
        return result;
    }

    public WorldScriptDefinition? Find(WorldDefinitionKind kind, string name) =>
        _records.FirstOrDefault(record => record.Kind == kind && WorldScriptNameComparer.Instance.Equals(record.Name, name));

    public void DefinePosition(string name, int x, int y, int radius = -1, int surface = -1)
    {
        if (name.Length == 0 || x == -1 || y == -1) return;
        var value = WorldScriptDefinition.Create(WorldDefinitionKind.Position, name, [x, y, radius, surface, 0, 0, 0, 0]);
        var index = _records.FindIndex(record => record.Kind == WorldDefinitionKind.Position && WorldScriptNameComparer.Instance.Equals(record.Name, name));
        if (index < 0) _records.Add(value);
        else _records[index] = value;
    }

    public void AppendPoolPosition(string name, int x, int y, int parameter2 = -1, int parameter3 = -1)
    {
        if (name.Length == 0 || x == -1 || y == -1) return;
        _records.Add(WorldScriptDefinition.Create(WorldDefinitionKind.PoolPosition, name, [x, y, parameter2, parameter3, 0, 0, 0, 0]));
    }

    /// <param name="random">Gold rand() results in 0..32767. A randomized definition requires an explicit source.</param>
    public SacredScriptPosition ResolvePosition(string name, Func<int>? random = null, Func<int, int, bool>? positionAllowed = null)
    {
        var definition = Find(WorldDefinitionKind.Position, name) ?? throw new InvalidDataException($"Unresolved position '{name}'.");
        var x = definition.Parameter(0);
        var y = definition.Parameter(1);
        var radius = definition.Parameter(2);
        if (radius > 0)
        {
            if (random is null || positionAllowed is null)
                throw new InvalidDataException($"Position '{name}' requires native random sampling and tile validation (radius {radius}).");
            var width = unchecked(radius * 2);
            if (width < 0) width = 1;
            var range = unchecked(width + 1);
            // Gold 0x47455B..0x474600 tests at most ten candidates, retaining the final one if all fail.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                x = unchecked(definition.Parameter(0) + Next() % range - radius);
                y = unchecked(definition.Parameter(1) + Next() % range - radius);
                if (positionAllowed(x, y)) break;
            }
        }
        return new(x, y, Math.Max(0, definition.Parameter(3)));
        int Next()
        {
            var value = random!();
            if (value is < 0 or > 32767) throw new InvalidDataException("Native rand() result must be in 0..32767.");
            return value;
        }
    }
}
