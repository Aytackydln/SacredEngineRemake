using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Verified Speed Up initializer input; +0x20 scales its retirement timer, not frame changes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredSpeedUpFxEventArgumentsLayout
{
    public const int SerializedSize = 0x24;
    [FieldOffset(0x20)] public float ElapsedRate;
}
