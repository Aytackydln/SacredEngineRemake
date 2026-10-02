namespace Sacred.Particles;

/// <summary>Auto prefers a ready GPU renderer; CPU SIMD retains its scalar fallback.</summary>
public enum ParticleSimulationMode
{
    CpuSimd,
    CpuScalar,
    Gpu,
    Auto
}
