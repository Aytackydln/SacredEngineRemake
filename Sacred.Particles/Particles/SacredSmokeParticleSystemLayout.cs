using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Recovered fields of the ORIGINAL 32-bit cParticleSystem_smoke object
/// (constructor 0x76E200). This is a memory layout, not an Items.pak record.
/// Texture handles are resolved by native name lookup and are not fixed texture IDs.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredSmokeParticleSystemLayout
{
    public const int SerializedSize = 0x2E4C;
    [FieldOffset(0x0000)] public readonly uint NativeVtableAddress;
    [FieldOffset(0x20A0)] public readonly SacredSmokeParticleStateLayout State;
    /// <summary>Resolved from PARTICLE_FIRE03.TGA.</summary>
    [FieldOffset(0x2E3C)] public readonly uint FireTextureHandle;
    /// <summary>Resolved from PARTICLE_SMOKE03.TGA.</summary>
    [FieldOffset(0x2E40)] public readonly uint SmokeTextureHandle;
    /// <summary>Resolved from PARTICLE_WATER01.TGA.</summary>
    [FieldOffset(0x2E44)] public readonly uint WaterTextureHandle;
    /// <summary>Resolved from PARTICLE_MULTI02.TGA.</summary>
    [FieldOffset(0x2E48)] public readonly uint MultiTextureHandle;
}
