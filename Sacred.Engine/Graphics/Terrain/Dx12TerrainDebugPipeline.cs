using System;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Terrain;

internal sealed class Dx12TerrainDebugPipeline : IDisposable
{
    public Dx12TerrainDebugPipeline(ID3D12Device device, Format format)
    {
        var parameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 4), ShaderVisibility.All),
            new RootParameter(RootParameterType.ShaderResourceView, new RootDescriptor(0, 0), ShaderVisibility.Vertex),
            new RootParameter(new RootDescriptorTable
            {
                Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 1, 0, 0)]
            }, ShaderVisibility.Pixel)
        };
        var description = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout, parameters, []);
        RootSignature = device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        try
        {
            var blend = BlendDescription.AlphaBlend;
            blend.RenderTarget[0].SourceBlend = Blend.One;
            blend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
            blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
            Pipeline = device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = RootSignature,
                VertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.TerrainDebugVertexShader),
                PixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.TerrainDebugPixelShader),
                BlendState = blend,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = DepthStencilDescription.None,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = [format],
                DepthStencilFormat = Format.Unknown,
                SampleDescription = new SampleDescription(1, 0)
            });
        }
        catch
        {
            RootSignature.Dispose();
            throw;
        }
    }

    public ID3D12RootSignature RootSignature { get; }
    public ID3D12PipelineState Pipeline { get; }

    public void Dispose()
    {
        Pipeline.Dispose();
        RootSignature.Dispose();
    }
}
