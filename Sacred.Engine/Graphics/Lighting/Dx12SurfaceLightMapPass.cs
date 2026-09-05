using System;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Lighting;

/// <summary>Accumulates visible local illumination into a screen-space texture.</summary>
internal sealed class Dx12SurfaceLightMapPass : IDisposable
{
    public const Format TextureFormat = Format.R8_UNorm;

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvCpuHandle;
    private readonly GpuDescriptorHandle _srvGpuHandle;
    private readonly ID3D12DescriptorHeap _rtvHeap;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipeline;
    private ID3D12Resource? _texture;
    private int _width;
    private int _height;

    public Dx12SurfaceLightMapPass(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        CpuDescriptorHandle srvCpuHandle,
        GpuDescriptorHandle srvGpuHandle)
    {
        _device = device;
        _commandList = commandList;
        _srvCpuHandle = srvCpuHandle;
        _srvGpuHandle = srvGpuHandle;
        _rtvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.RenderTargetView,
            1,
            DescriptorHeapFlags.None,
            0));
    }

    public GpuDescriptorHandle ShaderResourceHandle => _srvGpuHandle;

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline)
    {
        _rootSignature = pipeline.RootSignature;
        _pipeline = pipeline[Dx12PipelineKind.SurfaceLightMap];
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public unsafe void Record(
        int surfaceLightCount,
        float nightBlend,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        EnsureTexture(renderWidth, renderHeight);
        var texture = _texture
                      ?? throw new InvalidOperationException("The surface-light map has not been created.");

        Dx12TextureUploader.Transition(
            _commandList,
            texture,
            ResourceStates.PixelShaderResource,
            ResourceStates.RenderTarget);

        var renderTarget = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        _commandList.RSSetViewports(new Viewport(0, 0, renderWidth, renderHeight, 0.0f, 1.0f));
        _commandList.RSSetScissorRects(new RawRect(0, 0, renderWidth, renderHeight));
        _commandList.OMSetRenderTargets(renderTarget, null);
        _commandList.ClearRenderTargetView(renderTarget, new Color4(0.0f, 0.0f, 0.0f, 0.0f));

        if (surfaceLightCount > 0 && _pipeline is not null && _rootSignature is not null)
        {
            var constants = stackalloc float[SurfaceLightMapShaderLayout.SceneConstantsCount];
            SurfaceLightMapShaderConstantsWriter.Write(
                constants,
                new SurfaceLightMapSceneConstants(
                    new Vector2(renderWidth, renderHeight),
                    nightBlend));

            _commandList.SetGraphicsRootSignature(_rootSignature);
            _commandList.SetPipelineState(_pipeline);
            _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            _commandList.SetGraphicsRoot32BitConstants(
                SurfaceLightMapShaderLayout.SceneConstantsRootParameter,
                SurfaceLightMapShaderLayout.SceneConstantsCount,
                constants,
                0);
            _commandList.SetGraphicsRootShaderResourceView(
                SurfaceLightMapShaderLayout.InstanceBufferRootParameter,
                frame.LightHaloInstanceBuffer.GPUVirtualAddress);
            _commandList.DrawInstanced(4, (uint)surfaceLightCount, 0, 0);
        }

        Dx12TextureUploader.Transition(
            _commandList,
            texture,
            ResourceStates.RenderTarget,
            ResourceStates.PixelShaderResource);
    }

    private void EnsureTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_texture is not null && _width == width && _height == height)
            return;

        _texture?.Dispose();
        _width = width;
        _height = height;
        var description = new ResourceDescription(
            ResourceDimension.Texture2D,
            0,
            (ulong)width,
            (uint)height,
            1,
            1,
            TextureFormat,
            1,
            0,
            TextureLayout.Unknown,
            ResourceFlags.AllowRenderTarget);
        _texture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.PixelShaderResource,
            null);
        _device.CreateRenderTargetView(_texture, null, _rtvHeap.GetCPUDescriptorHandleForHeapStart());
        _device.CreateShaderResourceView(_texture, null, _srvCpuHandle);
        EngineLog.WriteLine($"Surface light map created: {width}x{height} R8_UNorm.");
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _rtvHeap.Dispose();
    }
}
