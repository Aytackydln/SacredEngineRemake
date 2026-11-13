using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold's ordered wea.bin pools and opcode-114 appends. Reset and
/// post-StartCode reload replace all 256 slots; declarations address only 255.
/// This is metadata, separate from item construction and spawn-equipment rules.</summary>
public sealed class WorldScriptEquipmentPools
{
    public const int PoolCount = SacredScriptEquipmentPoolLayout.DeclarationIndexLimit;
    private readonly Dictionary<int, List<uint>> _pools = [];
    public WorldScriptEquipmentPools() { }
    public WorldScriptEquipmentPools(SacredScriptEquipmentPoolArchive source) => Reload(source);
    public int EntryCount { get; private set; }
    /// <summary>Diagnostic counts, not serialized native fields.</summary>
    public int DeclarationCount { get; private set; }
    public int PeakEntryCount { get; private set; }
    public IReadOnlyList<uint> Get(int index) => _pools.TryGetValue(index, out var pool) ? pool.AsReadOnly() : Array.Empty<uint>();
    /// <summary>Original loader replaces row lengths and values, preserving
    /// duplicates and unsigned file entries. Earlier declarations do not survive.</summary>
    public void Reload(SacredScriptEquipmentPoolArchive source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _pools.Clear();
        for (var i = 0; i < SacredScriptEquipmentPoolLayout.FilePoolCount; i++)
            if (source.Get(i).Count != 0) _pools.Add(i, source.Get(i).ToList());
        EntryCount = source.EntryCount;
        PeakEntryCount = Math.Max(PeakEntryCount, EntryCount);
        Console.WriteLine($"Script equipment pools reloaded: {EntryCount} entries across 256 slots.");
    }
    public void Apply(SacredScriptCommand command)
    {
        var index = -1;
        var type = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.Integer)
                index = operand.Integer ?? throw new InvalidDataException("Equipment pool index requires native variable expression resolution.");
            else if (operand.Kind == SacredScriptArgumentKind.TypeId) type = operand.Integer!.Value;
        }
        if (index < 0 || index >= PoolCount || type <= 0) return;
        if (!_pools.TryGetValue(index, out var values)) _pools[index] = values = [];
        values.Add((uint)type);
        DeclarationCount++;
        EntryCount++;
        PeakEntryCount = Math.Max(PeakEntryCount, EntryCount);
    }
}
