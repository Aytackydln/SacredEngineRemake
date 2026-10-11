using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold serializers 7899A0/789BB0 write 0x10 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredElementStoneFxStateLayout
{
    public const int SerializedSize = 0x10;
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04)] public readonly byte Active;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown006;
    /// <summary>Event +38: native selectors 1..7; restored state reloads its model/texture.</summary>
    [FieldOffset(0x08)] public readonly uint Element;
    /// <summary>Increments by dt; subtracts one full turn only when strictly greater.</summary>
    [FieldOffset(0x0C)] public readonly float Rotation;
}
