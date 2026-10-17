using System.Runtime.InteropServices;
using Sacred.Core.World;

namespace Sacred.Assets.World;

/// <summary>Original Triggers.PAK header/descriptor and native trigger vector, without script mutations.</summary>
public sealed class SacredWorldTriggerArchive
{
    private SacredWorldTriggerArchive(SacredWorldTriggerPakHeaderLayout header, WorldFileChunkLayout descriptor,
        SacredWorldTriggerRecordLayout[] records)
    { Header = header; Descriptor = descriptor; Records = Array.AsReadOnly(records); }
    public SacredWorldTriggerPakHeaderLayout Header { get; }
    /// <summary>Retained descriptor. Native loading reads records immediately after it, without seeking to Position.</summary>
    public WorldFileChunkLayout Descriptor { get; }
    public IReadOnlyList<SacredWorldTriggerRecordLayout> Records { get; }
    public static SacredWorldTriggerArchive Load(string path) => FromPayload(File.ReadAllBytes(path));
    public static SacredWorldTriggerArchive FromPayload(ReadOnlySpan<byte> bytes)
    {
        const int start = SacredWorldTriggerPakHeaderLayout.SerializedSize + WorldFileChunkLayout.SerializedSize;
        if (bytes.Length < start) throw new InvalidDataException("Truncated Triggers.PAK header/descriptor.");
        var header = MemoryMarshal.Read<SacredWorldTriggerPakHeaderLayout>(bytes);
        if ((header.SignatureAndVersion & 0xffffff) != SacredWorldTriggerPakHeaderLayout.Signature)
            throw new InvalidDataException("Invalid Triggers.PAK signature.");
        if (header.TriggerCount > (bytes.Length - start) / SacredWorldTriggerRecordLayout.SerializedSize)
            throw new InvalidDataException("Truncated Triggers.PAK native trigger table.");
        var descriptor = MemoryMarshal.Read<WorldFileChunkLayout>(bytes[SacredWorldTriggerPakHeaderLayout.SerializedSize..]);
        var records = MemoryMarshal.Cast<byte, SacredWorldTriggerRecordLayout>(bytes.Slice(start, checked((int)header.TriggerCount * SacredWorldTriggerRecordLayout.SerializedSize))).ToArray();
        Console.WriteLine($"World triggers loaded: {records.Length} original records, format {header.Version}.");
        return new(header, descriptor, records);
    }
}
