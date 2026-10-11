using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7C5230/7C5350 serialize 0x924 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredUnderworldGlyphFxStateLayout
{
    public const int SerializedSize = 0x924;
    public const int WaveCount = 20;
    public const int PointsPerWave = 9;
    [FieldOffset(0x000)] public float ElapsedTime;
    [FieldOffset(0x004)] public byte Active;
    [FieldOffset(0x005), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x006), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x008)] public uint Element;
    [FieldOffset(0x00C)] public float Rotation;
    [FieldOffset(0x010)] public SacredGlyphPointsLayout Points;
    /// <summary>Original rand initialization: rate origin minus normalized rand * width.</summary>
    [FieldOffset(0x880)] public SacredGlyphWaveValuesLayout WaveRates;
    [FieldOffset(0x8D0)] public SacredGlyphWaveValuesLayout WaveRadii;
    /// <summary>Renderer multiplies the normalized side vector by this value and its native half-width.</summary>
    [FieldOffset(0x920)] public float Thickness;
}
