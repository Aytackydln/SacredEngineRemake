using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Native 0x64-byte spawn-parameter block consumed by Sacred.exe 0x764760.
/// Included verbatim in the smoke system's serialized state; presets originate in
/// executable code, not Items.pak. Scalar size is NOT a measured pixel diameter.
/// Fields named RandomWidth are half-ranges: base + (2 * rand/32767 - 1) * value
/// (0x765590..0x76587D), not a total width.</summary>
/// <remarks>demo: PS_STD_CREATION. Original names in order: mass, dMass,
/// size, dSize, phi, dPhi, moment, dMoment, velo, dVelo, pos, dPos,
/// color, dColor, releaseIntervall, maxAnz, frameAnz. Last three bytes are padding.</remarks>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredParticleEmissionLayout
{
    public const int SerializedSize = 0x64;
    /// <summary>Acceleration scalar. Update subtracts dt * gravity from vertical velocity
    /// and adds dt * gravity * the motion block's GravityDirection to velocity.</summary>
    [FieldOffset(0x00)] public readonly float Gravity;
    [FieldOffset(0x04)] public readonly float GravityRandomWidth;
    [FieldOffset(0x08)] public readonly float Size;
    [FieldOffset(0x0C)] public readonly float SizeRandomWidth;
    [FieldOffset(0x10)] public readonly float Rotation;
    [FieldOffset(0x14)] public readonly float RotationRandomWidth;
    [FieldOffset(0x18)] public readonly float AngularVelocity;
    [FieldOffset(0x1C)] public readonly float AngularVelocityRandomWidth;
    [FieldOffset(0x20)] public readonly SacredParticleVectorLayout Velocity;
    [FieldOffset(0x2C)] public readonly SacredParticleVectorLayout VelocityRandomWidth;
    [FieldOffset(0x38)] public readonly SacredParticleVectorLayout PositionOffset;
    [FieldOffset(0x44)] public readonly SacredParticleVectorLayout PositionRandomWidth;
    /// <summary>Packed color copied to particle +0x34 when the +0x54 parameter is zero.</summary>
    [FieldOffset(0x50)] public readonly uint Color;
    /// <summary>Packed AARRGGBB per-channel random half-ranges. The generator unpacks
    /// each byte, samples base + (2 * rand/32767 - 1) * range, truncates and packs
    /// the channels (0x764828..0x7648E8, 0x76585E..0x765972).</summary>
    [FieldOffset(0x54)] public readonly uint ColorRandomWidth;
    /// <summary>Time between spawns; the generator divides elapsed time by this value.
    /// Zero selects its count-based path. Time unit follows the native simulation clock.</summary>
    [FieldOffset(0x58)] public readonly float EmissionInterval;
    /// <summary>Count limit in the zero-interval generation path (0x764DFF).</summary>
    [FieldOffset(0x5C)] public readonly uint BurstCount;
    /// <summary>0 selects variant 0; 0xFF uses the selected parameter-set index;
    /// other values enter random variant selection (0x764DC2).</summary>
    [FieldOffset(0x60)] public readonly byte VariantSelection;
    [FieldOffset(0x61), BinaryUnknown] public readonly byte Unknown61;
    [FieldOffset(0x62), BinaryUnknown] public readonly ushort Unknown62;
}
