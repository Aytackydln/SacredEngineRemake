using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Confusion's 68 saved bytes at native object +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredConfusionFxStateLayout
{
    public const int SerializedSize = 0x44;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float Lifetime;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09)] public byte Unknown09;
    [FieldOffset(0x0A)] public ushort Unknown0A;
    [FieldOffset(0x0C)] public SacredHelicalRibbonParametersLayout Ribbon;
    [FieldOffset(0x34)] public float RibbonPhase;
    [FieldOffset(0x38)] public float GroundOffsetX;
    [FieldOffset(0x3C)] public float GroundOffsetY;
    [FieldOffset(0x40)] public float GroundHeight;
}
