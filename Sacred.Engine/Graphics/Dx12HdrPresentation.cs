using System;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Encodes the completed floating-point RGB frame into the native HDR10 swapchain.</summary>
internal sealed class Dx12HdrPresentation : IDisposable
{
    private readonly ID3D12Resource[] _targets = new ID3D12Resource[Dx12DeviceContext.FrameCount];
    private readonly ID3D12DescriptorHeap _rtvs;
    private readonly ID3D12DescriptorHeap _srvs;
    private readonly ID3D12DescriptorHeap[] _heaps;
    private readonly int _rtvSize;
    private readonly int _srvSize;
    private readonly ID3D12RootSignature _root;
    private readonly ID3D12PipelineState _pipeline;
    public float FrameWhiteNits { get; set; } = HdrBrightnessSettings.DefaultSceneBrightnessNits;

    public Dx12HdrPresentation(ID3D12Device device, Dx12SwapChain swapChain)
    {
        _rtvs = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView,
            Dx12DeviceContext.FrameCount));
        _srvs = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            Dx12DeviceContext.FrameCount, DescriptorHeapFlags.ShaderVisible));
        _heaps = [_srvs];
        _rtvSize = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srvSize = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        var description = new RootSignatureDescription(RootSignatureFlags.None,
            [new RootParameter(new RootDescriptorTable
            {
                Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)]
            }, ShaderVisibility.Pixel),
             new RootParameter(new RootConstants(0, 0, 1), ShaderVisibility.Pixel)], []);
        _root = device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        _pipeline = device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = _root,
            VertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.HdrPresentationVertexShader),
            PixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.HdrPresentationPixelShader),
            BlendState = Dx12BlendStates.Opaque,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Dx12HdrSwapChain.HdrBackBufferFormat],
            SampleDescription = new SampleDescription(1, 0)
        });
        for (var index = 0; index < _targets.Length; index++)
        {
            _targets[index] = swapChain.GetBuffer((uint)index);
            device.CreateRenderTargetView(_targets[index], null, Rtv(index));
        }
        EngineLog.WriteLine("HDR10 presentation ready: native RGB blending, one final gamma/Rec.2020/PQ conversion.");
    }

    public void BindSource(ID3D12Device device, int index, ID3D12Resource source) =>
        device.CreateShaderResourceView(source, null, _srvs.GetCPUDescriptorHandleForHeapStart() + index * _srvSize);

    public ID3D12Resource Record(ID3D12GraphicsCommandList commands, int index, ID3D12Resource source, int width, int height)
    {
        var target = _targets[index];
        Dx12TextureUploader.Transition(commands, source, ResourceStates.Present, ResourceStates.PixelShaderResource);
        Dx12TextureUploader.Transition(commands, target, ResourceStates.Present, ResourceStates.RenderTarget);
        commands.RSSetViewports(new Viewport(0, 0, width, height, 0, 1));
        commands.RSSetScissorRects(new RawRect(0, 0, width, height));
        commands.OMSetRenderTargets(Rtv(index), null);
        commands.SetDescriptorHeaps(1, _heaps);
        commands.SetGraphicsRootSignature(_root);
        commands.SetPipelineState(_pipeline);
        commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + index * _srvSize);
        commands.SetGraphicsRoot32BitConstant(1, BitConverter.SingleToUInt32Bits(FrameWhiteNits), 0);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.DrawInstanced(3, 1, 0, 0);
        Dx12TextureUploader.Transition(commands, target, ResourceStates.RenderTarget, ResourceStates.Present);
        Dx12TextureUploader.Transition(commands, source, ResourceStates.PixelShaderResource, ResourceStates.Present);
        return target;
    }

    private CpuDescriptorHandle Rtv(int index) => _rtvs.GetCPUDescriptorHandleForHeapStart() + index * _rtvSize;

    public void Dispose()
    {
        foreach (var target in _targets) target?.Dispose();
        _pipeline.Dispose();
        _root.Dispose();
        _srvs.Dispose();
        _rtvs.Dispose();
    }
}
