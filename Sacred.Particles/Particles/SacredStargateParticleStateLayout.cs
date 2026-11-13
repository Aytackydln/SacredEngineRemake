using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Original cParticleSystem_stargate +0x20A0 block. Native 0x7809E0
/// writes it to file streams; 0x780A80 writes it to full object packets.
/// Both prepend byte length 0x34 and follow it with 0xDEADC0DE/base state.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredStargateParticleStateLayout
{
    public const int SerializedSize = 0x34;
    [FieldOffset(0x00), BinaryUnknown] public readonly uint Unknown00;
    [FieldOffset(0x04), BinaryUnknown] public readonly uint Unknown04;
    [FieldOffset(0x08), BinaryUnknown] public readonly uint Unknown08;
    [FieldOffset(0x0C), BinaryUnknown] public readonly uint Unknown0C;
    /// <summary>Constructor/start initialize both angle words to pi/4.</summary>
    [FieldOffset(0x10)] public readonly float Angle0;
    [FieldOffset(0x14)] public readonly float Angle1;
    [FieldOffset(0x18), BinaryUnknown] public readonly uint Unknown18;
    [FieldOffset(0x1C), BinaryUnknown] public readonly uint Unknown1C;
    /// <summary>Set to one by construction and Start (0x780BB0).</summary>
    [FieldOffset(0x20)] public readonly byte Started;
    [FieldOffset(0x21), BinaryUnknown] public readonly byte Unknown21;
    [FieldOffset(0x22), BinaryUnknown] public readonly ushort Unknown22;
    /// <summary>Two successive original CRT rand modulo-16 selections; Start
    /// replaces the constructor's pair. They select the loaded minimap slots.</summary>
    [FieldOffset(0x24)] public readonly uint MinimapSlot0;
    [FieldOffset(0x28)] public readonly uint MinimapSlot1;
    [FieldOffset(0x2C), BinaryUnknown] public readonly uint Unknown2C;
    [FieldOffset(0x30), BinaryUnknown] public readonly uint Unknown30;
}
