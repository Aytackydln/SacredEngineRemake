using Sacred.Core.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Networking;

public static class WorldSettingsFileSaver
{
    /// <summary>Complete Gold 0x662AD0 at explicit file/string storage boundaries.
    /// A fixed snapshot would lose later value/delimiter changes during writing.</summary>
    public static void Save(SacredSettingsSaveDefinition definition, SacredSettingsDocument document,
        Func<ReadOnlyMemory<byte>> readDelimiter, WorldSettingsSaveServices services)
    {
        var path = document.Path; var nul = path.Span.IndexOf((byte)0);
        var handle = services.Open(nul < 0 ? path : path[..nul], definition.OpenMode);
        if (handle == 0) return;
        foreach (var pair in document.Values.EnumerateEntries())
            _ = services.Write(handle, SacredSettingsRecordFormatter.Format(definition.RecordFormat.Span, pair.Key, readDelimiter(), pair.Value));
        _ = services.Close(handle);
    }
}
