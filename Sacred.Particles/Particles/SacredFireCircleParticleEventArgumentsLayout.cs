using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Verified Fire Circle event inputs. Combat-art parameter zero supplies duration.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredFireCircleParticleEventArgumentsLayout
{
    public const int SerializedSize = 0x78;
    [FieldOffset(0x20)] public SacredParticleVectorLayout SavedVector;
    /// <summary>Zero requests audio; nonzero bypasses that call.</summary>
    [FieldOffset(0x38)] public uint AudioSelector;
    [FieldOffset(0x40)] public ushort CombatArtLevel;
    [FieldOffset(0x70)] public float DurationMultiplier;
    [FieldOffset(0x74)] public uint CombatArtAddress;
}
