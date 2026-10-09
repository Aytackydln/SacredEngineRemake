using System.Linq;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace AssetViewer.ItemViewer;

internal static class Dx12FxPipelineDefinitions
{
    public static Dx12PipelineGroupDefinition AddAlphaBlend(Dx12PipelineGroupDefinition group)
    {
        var particle = group.Pipelines[Dx12PipelineKind.TransparentItemParticle];
        var blend = particle.BlendState;
        blend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
        blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        var alpha = new Dx12GraphicsPipelineDefinition(Dx12PipelineKind.FxParticle,
            particle.VertexShader, particle.PixelShader, particle.InputLayout,
            blend, particle.RasterizerState, particle.DepthStencilState, particle.UsesDepthBuffer);
        return new(group.RootParameters, group.StaticSamplers, group.Pipelines.Values.Append(alpha), group.RootSignatureFlags);
    }
}
