using System;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Alpha-weighted area reduction of a completed GPU frame before readback.</summary>
internal sealed class Dx12AreaDownsamplePass : IDisposable
{
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipeline;

    public Dx12AreaDownsamplePass(ID3D12Device device, Format outputFormat)
    {
        var parameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 4), ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable
            {
                Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)]
            }, ShaderVisibility.Pixel)
        };
        var description = new RootSignatureDescription(RootSignatureFlags.None, parameters, []);
        _rootSignature = device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        try
        {
            _pipeline = device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = _rootSignature,
                VertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.AreaDownsampleVertexShader),
                PixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.AreaDownsamplePixelShader),
                BlendState = BlendDescription.Opaque,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = DepthStencilDescription.None,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = [outputFormat],
                DepthStencilFormat = Format.Unknown,
                SampleDescription = new SampleDescription(1, 0)
            });
        }
        catch
        {
            _rootSignature.Dispose();
            throw;
        }
    }

    public void Record(Dx12DeviceContext graphics)
    {
        var commands = graphics.CommandList;
        Dx12TextureUploader.Transition(commands, graphics.CurrentBackBuffer,
            ResourceStates.Present, ResourceStates.RenderTarget);
        commands.RSSetViewports(new Viewport(0, 0, graphics.OutputWidth, graphics.OutputHeight, 0, 1));
        commands.RSSetScissorRects(new RawRect(0, 0, graphics.OutputWidth, graphics.OutputHeight));
        commands.OMSetRenderTargets(graphics.CurrentRenderTarget, null);
        commands.SetDescriptorHeaps(1, graphics.ShaderVisibleDescriptorHeaps);
        commands.SetGraphicsRootSignature(_rootSignature);
        commands.SetPipelineState(_pipeline);
        commands.SetGraphicsRoot32BitConstant(0, (uint)graphics.RenderWidth, 0);
        commands.SetGraphicsRoot32BitConstant(0, (uint)graphics.RenderHeight, 1);
        commands.SetGraphicsRoot32BitConstant(0, (uint)graphics.OutputWidth, 2);
        commands.SetGraphicsRoot32BitConstant(0, (uint)graphics.OutputHeight, 3);
        commands.SetGraphicsRootDescriptorTable(1, graphics.SceneColorSrvGpuHandle);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.DrawInstanced(3, 1, 0, 0);
        Dx12TextureUploader.Transition(commands, graphics.CurrentBackBuffer,
            ResourceStates.RenderTarget, ResourceStates.Present);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _rootSignature.Dispose();
    }
}
