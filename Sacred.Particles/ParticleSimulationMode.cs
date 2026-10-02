namespace Sacred.Particles;

/// <summary>CPU simulation choices; SIMD falls back to scalar on unsupported hardware.</summary>
public enum ParticleSimulationMode
{
    CpuSimd,
    CpuScalar
}
