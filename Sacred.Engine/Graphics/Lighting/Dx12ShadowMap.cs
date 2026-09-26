using System;
using Vortice;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Lighting;

/// <summary>Collects all shadow coverage with max blending before one scene composition.</summary>
internal sealed class Dx12ShadowMap : IDisposable
{
    public const Format TextureFormat = Format.R8_UNorm;

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvCpuHandle;
    private readonly GpuDescriptorHandle _srvGpuHandle;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private ID3D12Resource? _texture;
    private int _width;
    private int _height;

    public Dx12ShadowMap(
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
            DescriptorHeapType.RenderTargetView, 1, DescriptorHeapFlags.None, 0));
    }

    public GpuDescriptorHandle ShaderResourceHandle => _srvGpuHandle;

    public void Begin(int width, int height)
    {
        EnsureTexture(width, height);
        var texture = _texture ?? throw new InvalidOperationException("Shadow map was not created.");
        Dx12TextureUploader.Transition(
            _commandList, texture, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        var target = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        _commandList.RSSetViewports(new Viewport(0, 0, width, height, 0.0f, 1.0f));
        _commandList.RSSetScissorRects(new RawRect(0, 0, width, height));
        _commandList.OMSetRenderTargets(target, null);
        _commandList.ClearRenderTargetView(target, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
    }

    public void End()
    {
        if (_texture is null)
            return;
        Dx12TextureUploader.Transition(
            _commandList, _texture, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
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
        _texture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default, 0, 0),
            HeapFlags.None,
            new ResourceDescription(
                ResourceDimension.Texture2D, 0, (ulong)width, (uint)height, 1, 1,
                TextureFormat, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource,
            null);
        _device.CreateRenderTargetView(_texture, null, _rtvHeap.GetCPUDescriptorHandleForHeapStart());
        _device.CreateShaderResourceView(_texture, null, _srvCpuHandle);
        EngineLog.WriteLine($"Shadow map created: {width}x{height} R8_UNorm.");
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _rtvHeap.Dispose();
    }
}
