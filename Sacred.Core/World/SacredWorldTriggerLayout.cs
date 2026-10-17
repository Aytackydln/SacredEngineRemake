using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>Gold cTrigger's original cFile serialization (0x4165A0). The world bank uses
/// the same 20-byte record; runtime field order differs from serialized field order.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredWorldTriggerLayout
{
    public const int SerializedSize = 20;
    public const uint FormatMarker = 0xDEAFFEED;
    public const ushort OpenStateMask = 1;
    [FieldOffset(0)] public readonly uint Marker;
    [FieldOffset(4)] public readonly uint Reference;
    /// <summary>Native +0x04; dynamically created triggers include flag 0x80.
    /// Bits 0x04/0x40 select activation callbacks and a 1000 ms lock rather than ordinary bit updates.</summary>
    [FieldOffset(8)] public readonly ushort Flags;
    /// <summary>Native +0x0A. Bit 0 (mask 1) is the door open state tested by Gold
    /// client 0x7DF587/0x7DF64C. Bit 0x4000 locks ordinary transitions; bit 0x8000
    /// suppresses setting mask 1 and ordinary clearing. Clear first removes either
    /// control bit explicitly included in its mask (Gold 0x415F00/0x416280).</summary>
    [FieldOffset(10)] public readonly ushort State;
    public bool IsOpen => (State & OpenStateMask) != 0;
    /// <summary>Native +0x06, serialized after State.</summary>
    [FieldOffset(12)] public readonly uint Referent;
    /// <summary>Native +0x0C. Nonzero callback activation queries this requirement
    /// against the local player's inventory before scheduling; a successful query clears it.</summary>
    [FieldOffset(16)] public readonly uint Parameter;
}
