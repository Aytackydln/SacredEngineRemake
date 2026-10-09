using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Teleport serializer 0x771C80 copies 0x40 bytes from object +0x20A0.
/// Its manual updater 0x771410 does not use standard emission/motion blocks.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredTeleportParticleStateLayout
{
    public const int SerializedSize = 0x40;
    [FieldOffset(0x00)] public uint ParticleCount;
    [FieldOffset(0x04)] public float ElapsedTime;
    [FieldOffset(0x08)] public float EnvelopeHeight;
    [FieldOffset(0x0C)] public float EnvelopeAngle;
    /// <summary>0 expand angle, 1 expand height, 2 jump wait, 3 return wait,
    /// 4 collapse height, 5 collapse angle, 6 dead (0x7AC790).</summary>
    [FieldOffset(0x10)] public uint Stage;
    [FieldOffset(0x14)] public float WaitToJump;
    /// <summary>Event +0x2C; scales physics and stages, but not ElapsedTime.</summary>
    [FieldOffset(0x18)] public float CastSpeed;
    [FieldOffset(0x1C)] public uint ActorId;
    [FieldOffset(0x20)] public ushort DestinationMap;
    [FieldOffset(0x22), BinaryUnknown] public ushort DestinationPadding22;
    [FieldOffset(0x24)] public int DestinationX;
    [FieldOffset(0x28)] public int DestinationY;
    [FieldOffset(0x2C)] public byte DestinationZ;
    [FieldOffset(0x2D), BinaryUnknown] public byte DestinationPadding2D;
    [FieldOffset(0x2E), BinaryUnknown] public ushort DestinationPadding2E;
    [FieldOffset(0x30)] public uint CornerColor0;
    [FieldOffset(0x34)] public uint CornerColor1;
    [FieldOffset(0x38)] public uint CornerColor2;
    [FieldOffset(0x3C)] public uint CornerColor3;
}
