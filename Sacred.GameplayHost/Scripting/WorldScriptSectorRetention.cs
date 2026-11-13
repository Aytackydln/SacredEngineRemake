namespace Sacred.GameplayHost.Scripting;

public sealed record WorldScriptSectorRetentionPlan(IReadOnlyList<uint> Retained, IReadOnlyList<uint> Unload);

/// <summary>Gold 0x634340 cache threshold, protected-sector selection and swap-removal order.</summary>
public static class WorldScriptSectorRetention
{
    public const uint LoadedFlag = 0x10000;
    public const uint ProtectedFlag = 0x800;
    public const int CapacityThreshold = 96;

    public static WorldScriptSectorRetentionPlan Plan(IReadOnlyList<uint> retained, IReadOnlyDictionary<uint,uint> flags)
    {
        var current = retained.ToList(); var unload = new List<uint>();
        if (current.Count < CapacityThreshold) return new(current,unload);
        var loaded = flags.Where(pair => (pair.Value & LoadedFlag) != 0).Select(pair => pair.Key).ToHashSet();
        for (var index=0;index<current.Count;)
        {
            var id = current[index];
            if (flags.TryGetValue(id,out var value) && (value & ProtectedFlag) != 0) { index++; continue; }
            if (loaded.Remove(id)) unload.Add(id);
            current[index] = current[^1]; current.RemoveAt(current.Count-1);
        }
        // The native fallback scan walks the complete sector table by ID.
        foreach (var id in loaded.Order())
            if ((flags[id] & ProtectedFlag) == 0) unload.Add(id);
        return new(current,unload);
    }
}
