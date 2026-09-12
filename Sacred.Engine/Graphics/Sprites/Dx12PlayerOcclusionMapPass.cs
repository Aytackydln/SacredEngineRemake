using System;
using System.Numerics;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Renders player-centered fade coverage into the texture sampled by late static sprites.</summary>
internal sealed class Dx12PlayerOcclusionMapPass : IDisposable
{
    public const Format TextureFormat = Format.R8_UNorm;

    private const float RadiusViewportFraction = 0.15f;
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

    public Dx12PlayerOcclusionMapPass(
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
        _pipeline = pipeline[Dx12PipelineKind.PlayerOcclusionMap];
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public unsafe void Record(PlayerOcclusionProbe player, int renderWidth, int renderHeight)
    {
        EnsureTexture(renderWidth, renderHeight);
        var texture = _texture
            ?? throw new InvalidOperationException("The player-occlusion map has not been created.");
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

        if (player.IsActive && _pipeline is not null && _rootSignature is not null)
        {
            var constants = stackalloc float[PlayerOcclusionMapShaderLayout.SceneConstantsCount];
            PlayerOcclusionMapShaderConstantsWriter.Write(
                constants,
                new PlayerOcclusionMapSceneConstants(
                    new Vector2(renderWidth, renderHeight),
                    player.ScreenPosition,
                    renderHeight * RadiusViewportFraction));
            _commandList.SetGraphicsRootSignature(_rootSignature);
            _commandList.SetPipelineState(_pipeline);
            _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            _commandList.SetGraphicsRoot32BitConstants(
                PlayerOcclusionMapShaderLayout.SceneConstantsRootParameter,
                PlayerOcclusionMapShaderLayout.SceneConstantsCount,
                constants,
                0);
            _commandList.DrawInstanced(4, 1, 0, 0);
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
        EngineLog.WriteLine($"Player occlusion map created: {width}x{height} R8_UNorm.");
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _rtvHeap.Dispose();
    }
}
