using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Creates the pipeline that rasterizes static-object player-occlusion coverage.</summary>
public static class Dx12PlayerOcclusionMapPipelineCatalog
{
    public static Dx12PipelineGroupDefinition Create()
    {
        var rootParameters = new[]
        {
            new RootParameter(
                new RootConstants(
                    PlayerOcclusionMapShaderLayout.SceneConstantsRegister,
                    0,
                    PlayerOcclusionMapShaderLayout.SceneConstantsCount),
                ShaderVisibility.All)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [],
            [new Dx12GraphicsPipelineDefinition(
                Dx12PipelineKind.PlayerOcclusionMap,
                Dx12ShaderCatalog.PlayerOcclusionMapVertexShader,
                Dx12ShaderCatalog.PlayerOcclusionMapPixelShader,
                null,
                BlendDescription.Opaque,
                RasterizerDescription.CullNone,
                DepthStencilDescription.None,
                usesDepthBuffer: false)]);
    }
}
