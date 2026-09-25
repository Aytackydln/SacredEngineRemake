using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Owns the output-resolution history sampled by the world-only temporal reconstruction pass.</summary>
internal sealed class Dx12Fsr2History : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvHandle;
    private ID3D12Resource? _texture;
    private int _width;
    private int _height;
    private int _inputWidth;
    private int _inputHeight;
    private Format _format;

    public Dx12Fsr2History(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        CpuDescriptorHandle srvHandle)
    {
        _device = device;
        _commandList = commandList;
        _srvHandle = srvHandle;
    }

    public bool IsValid { get; private set; }

    public void Ensure(int width, int height, int inputWidth, int inputHeight, Format format)
    {
        if (_texture is not null && _width == width && _height == height && _format == format)
        {
            if (_inputWidth != inputWidth || _inputHeight != inputHeight)
                IsValid = false;
            _inputWidth = inputWidth;
            _inputHeight = inputHeight;
            return;
        }

        _texture?.Dispose();
        var description = new ResourceDescription(
            ResourceDimension.Texture2D,
            0,
            (ulong)Math.Max(1, width),
            (uint)Math.Max(1, height),
            1,
            1,
            format,
            1,
            0,
            TextureLayout.Unknown,
            ResourceFlags.None);
        _texture = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.PixelShaderResource);
        _device.CreateShaderResourceView(_texture, null, _srvHandle);
        _width = width;
        _height = height;
        _inputWidth = inputWidth;
        _inputHeight = inputHeight;
        _format = format;
        IsValid = false;
    }

    public void Capture(ID3D12Resource output)
    {
        if (_texture is null)
            throw new InvalidOperationException("FSR 2 history has not been created.");

        Dx12TextureUploader.Transition(
            _commandList, output, ResourceStates.RenderTarget, ResourceStates.CopySource);
        Dx12TextureUploader.Transition(
            _commandList, _texture, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
        _commandList.CopyResource(_texture, output);
        Dx12TextureUploader.Transition(
            _commandList, _texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        Dx12TextureUploader.Transition(
            _commandList, output, ResourceStates.CopySource, ResourceStates.Present);
        IsValid = true;
    }

    public void Reset() => IsValid = false;

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        IsValid = false;
    }
}
