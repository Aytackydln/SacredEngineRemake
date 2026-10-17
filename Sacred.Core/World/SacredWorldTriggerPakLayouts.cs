using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>TRG header read by Gold 0x638570: 256 header bytes, then a 12-byte chunk descriptor.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredWorldTriggerPakHeaderLayout
{
    public const int SerializedSize = 256;
    public const uint Signature = 0x00475254; // ASCII TRG; the high byte is the version.
    [FieldOffset(0)] public readonly uint SignatureAndVersion;
    [FieldOffset(3)] public readonly byte Version;
    /// <summary>Native trigger vector length and the initial host 0x154 last-known bound.</summary>
    [FieldOffset(4)] public readonly uint TriggerCount;
}

/// <summary>Raw cTrigger records in Triggers.PAK; Gold 0x638570 copies each 16-byte record.
/// Unlike cFile save/bank serialization, this format has no per-record marker and stores Referent before State.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredWorldTriggerRecordLayout
{
    public const int SerializedSize = 16;
    [FieldOffset(0)] public readonly uint Reference;
    /// <summary>Bits 0x04/0x40 select callback activation; 0x80 enables publication.
    /// See Gold 0x415F00/0x416280 and the serialized SacredWorldTriggerLayout.</summary>
    [FieldOffset(4)] public readonly ushort Flags;
    [FieldOffset(6)] public readonly uint Referent;
    /// <summary>0x4000 is the transition lock; 0x8000 suppresses activation/ordinary clearing.</summary>
    [FieldOffset(10)] public readonly ushort State;
    /// <summary>Nonzero activation requirement queried against the local player's inventory.</summary>
    [FieldOffset(12)] public readonly uint Parameter;
}
