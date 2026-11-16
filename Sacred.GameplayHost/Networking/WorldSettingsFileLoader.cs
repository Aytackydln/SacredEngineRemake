using Sacred.Core.Executable;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;

public static class WorldSettingsFileLoader
{
    /// <summary>Complete Gold 0x662710 at explicit file/string-storage boundaries.
    /// No invented stream normalization, merge/default policy or error recovery.</summary>
    public static void Load(SacredSettingsFileDefinition definition, SacredSettingsDocument document,
        ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> keyDelimiters, WorldSettingsFileServices services)
    {
        document.Values.Clear();
        document.SetPath(path);
        var handle = services.Open(Z(path), definition.OpenMode);
        if (handle == 0) return;
        var capacity = definition.FirstReadCapacity;
        while (services.ReadLine(handle, capacity) is { } chunk)
        {
            if (chunk.Length >= capacity) throw new InvalidOperationException("Supplied fgets data exceeds its original capacity.");
            if (SacredSettingsLineParser.Parse(definition, chunk.Span, keyDelimiters.Span) is { } pair)
                document.Values.Insert(pair.Key, pair.Value);
            capacity = definition.NextReadCapacity;
        }
        services.Close(handle);
    }
    private static ReadOnlyMemory<byte> Z(ReadOnlyMemory<byte> value)
    {
        var end = value.Span.IndexOf((byte)0); return end < 0 ? value : value[..end];
    }
}
