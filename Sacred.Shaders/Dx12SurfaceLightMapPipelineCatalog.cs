using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Creates the pipeline used to accumulate screen-space surface illumination.</summary>
public static class Dx12SurfaceLightMapPipelineCatalog
{
    public static Dx12PipelineGroupDefinition Create()
    {
        var rootParameters = new[]
        {
            new RootParameter(
                new RootConstants(
                    SurfaceLightMapShaderLayout.SceneConstantsRegister,
                    0,
                    SurfaceLightMapShaderLayout.SceneConstantsCount),
                ShaderVisibility.All),
            new RootParameter(
                RootParameterType.ShaderResourceView,
                new RootDescriptor(SurfaceLightMapShaderLayout.InstanceBufferRegister, 0),
                ShaderVisibility.Vertex)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [],
            [new Dx12GraphicsPipelineDefinition(
                Dx12PipelineKind.SurfaceLightMap,
                Dx12ShaderCatalog.SurfaceLightMapVertexShader,
                Dx12ShaderCatalog.SurfaceLightMapPixelShader,
                null,
                CreateAdditiveBlend(),
                RasterizerDescription.CullNone,
                DepthStencilDescription.None,
                usesDepthBuffer: false)]);
    }

    private static BlendDescription CreateAdditiveBlend()
    {
        var blend = BlendDescription.AlphaBlend;
        blend.RenderTarget[0].SourceBlend = Blend.One;
        blend.RenderTarget[0].DestinationBlend = Blend.One;
        blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        blend.RenderTarget[0].DestinationBlendAlpha = Blend.One;
        return blend;
    }
}
