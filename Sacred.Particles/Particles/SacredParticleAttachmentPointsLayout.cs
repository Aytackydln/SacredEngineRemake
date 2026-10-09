using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Base-system vectors at +0x38. stdCreationOnLine indexes the first three
/// vectors; render helpers also add the independent +0x5C offset.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x30)]
public readonly struct SacredParticleAttachmentPointsLayout
{
    [FieldOffset(0)] public readonly SacredParticleVectorLayout First;
    [FieldOffset(0x0C)] public readonly SacredParticleVectorLayout Second;
    [FieldOffset(0x18)] public readonly SacredParticleVectorLayout Third;
    [FieldOffset(0x24)] public readonly SacredParticleVectorLayout RenderOffset;
}
