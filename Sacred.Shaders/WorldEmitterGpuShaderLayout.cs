using Vortice.Direct3D12;

namespace Sacred.Shaders;

public static class WorldEmitterGpuShaderLayout
{
    public const int ConstantCount = 8;
    private static byte[] Source() => EmbeddedShaderAssemblyReloader.ReadAllBytes(
        EmbeddedResource_Shaders.SacredWorldEmission_hlsl.GetResourceName());
    public static Dx12ShaderSource Shader(string entry) => new("SacredWorldEmission", [Source], entry, "cs_6_6") { StrictFloat = true };
    public static RootSignatureDescription Root() => new(RootSignatureFlags.None,
        [new(new RootConstants(0, 0, ConstantCount), ShaderVisibility.All),
         new(RootParameterType.ShaderResourceView, new RootDescriptor(0, 0), ShaderVisibility.All),
         new(RootParameterType.UnorderedAccessView, new RootDescriptor(0, 0), ShaderVisibility.All)], []);
}
