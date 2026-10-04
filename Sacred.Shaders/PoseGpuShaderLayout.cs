using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Byte offsets shared by source preparation and ordered pose compute kernels.</summary>
public static class PoseGpuShaderLayout
{
    public const int AnimationBoneBytes = 224;
    public const int SkinBoneBytes = 272;
    public const int ConstantCount = 8;
    public static Dx12ShaderSource Shader(string entry) => new("SacredGpuPose",
        [() => EmbeddedShaderAssemblyReloader.ReadAllBytes(EmbeddedResource_Shaders.SacredGpuPose_hlsl.GetResourceName())],
        entry, "cs_6_6") { StrictFloat = true };
    public static RootSignatureDescription Root() => new(RootSignatureFlags.None,
        [new(new RootConstants(0, 0, ConstantCount), ShaderVisibility.All),
         new(RootParameterType.ShaderResourceView, new RootDescriptor(0, 0), ShaderVisibility.All),
         new(RootParameterType.UnorderedAccessView, new RootDescriptor(0, 0), ShaderVisibility.All)], []);
}
