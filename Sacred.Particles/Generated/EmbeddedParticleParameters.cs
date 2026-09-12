using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles.Generated;

/// <summary>Decodes the original little-endian parameter bytes embedded in generated C#.</summary>
internal static class EmbeddedParticleParameters
{
    internal static IReadOnlyList<uint> ReadColors(string hex) =>
        Array.AsReadOnly(MemoryMarshal.Cast<byte, uint>(Convert.FromHexString(hex)).ToArray());

    internal static SacredParticleParameterSet Read(int index, string emissionHex, string motionHex)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Particle layouts require a little-endian host.");
        var emission = Convert.FromHexString(emissionHex);
        var motion = Convert.FromHexString(motionHex);
        if (emission.Length != SacredParticleEmissionLayout.SerializedSize || motion.Length != SacredParticleMotionLayout.SerializedSize)
            throw new InvalidDataException("Invalid embedded particle parameter block size.");
        return new SacredParticleParameterSet(index, MemoryMarshal.Read<SacredParticleEmissionLayout>(emission),
            MemoryMarshal.Read<SacredParticleMotionLayout>(motion));
    }
}
