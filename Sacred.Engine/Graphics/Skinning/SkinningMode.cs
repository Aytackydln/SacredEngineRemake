namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Skeletal deformation selection. GpuOnly skips unavailable draws instead of using CPU fallback.</summary>
public enum SkinningMode { Auto, Cpu, Gpu, GpuOnly }
