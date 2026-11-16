using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Networking;

public static class WorldStringSetting
{
    /// <summary>Compared complete ten Gold getters for importer arguments.
    /// Reads and returned values own their bytes; default writing never rereads.</summary>
    public static ReadOnlyMemory<byte> Read(SacredStringSettingDefinition definition,
        Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> readValue,
        Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> writeDefault)
    {
        var value = readValue(definition.ReadKey).ToArray();
        if (value.Length == 0)
        {
            if (definition.DefaultWriteValue is { } write) writeDefault(definition.DefaultKey, write.ToArray());
            return definition.EmptyReturnValue?.ToArray() ?? [];
        }
        if (definition.CdKey is not { } cdKey) return value;
        if ((uint)value.Length != cdKey.SeparatorBypassLength)
        {
            var separator = cdKey.Separator.Span;
            if (separator.IsEmpty) value = [];
            else
            {
                int found;
                while ((found = value.AsSpan().IndexOf(separator)) >= 0)
                    value = value.AsSpan(0, found).ToArray().Concat(value.Skip(found + 1)).ToArray();
            }
        }
        var begin = 0; var end = value.Length;
        while (begin < end && TrimByte(value[begin])) begin++;
        while (end > begin && TrimByte(value[end - 1])) end--;
        return value.AsMemory(begin, end - begin);
    }

    private static bool TrimByte(byte value) => value is 0x20 or 9 or 13;
}
