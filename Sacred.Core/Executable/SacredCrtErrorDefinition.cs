namespace Sacred.Core.Executable;

/// <summary>Ordered executable table and fallback ranges; all numeric mappings
/// come from the original image. No current TLS ownership is implied.</summary>
public sealed record SacredCrtErrorDefinition(IReadOnlyList<KeyValuePair<uint, uint>> Entries,
    uint AccessMinimum, uint AccessMaximum, uint AccessError,
    uint ExecutableMinimum, uint ExecutableMaximum, uint ExecutableError, uint DefaultError)
{
    public uint Map(uint error)
    {
        foreach (var entry in Entries) if (entry.Key == error) return entry.Value;
        if (error >= AccessMinimum && error <= AccessMaximum) return AccessError;
        if (error >= ExecutableMinimum && error <= ExecutableMaximum) return ExecutableError;
        return DefaultError;
    }
}
