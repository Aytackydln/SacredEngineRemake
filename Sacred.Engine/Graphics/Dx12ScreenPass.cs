using System;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Uploads and draws the current scene-owned full-screen image.</summary>
internal sealed class Dx12ScreenPass : IDisposable
{
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _uploader;
    private readonly CpuDescriptorHandle _cpuHandle;
    private readonly GpuDescriptorHandle _gpuHandle;
    private readonly WorldQuadShaderConstantsUpdater _constants = new();

    private ID3D12Resource? _texture;
    private ResourceStates _textureState = ResourceStates.Common;
    private int _width;
    private int _height;
    private readonly WeakReference<ScreenFrame> _preparedFrame = new(null!);

    public Dx12ScreenPass(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        CpuDescriptorHandle cpuHandle,
        GpuDescriptorHandle gpuHandle)
    {
        _commandList = commandList;
        _uploader = uploader;
        _cpuHandle = cpuHandle;
        _gpuHandle = gpuHandle;
    }

    public void Prepare(ScreenFrame frame, Dx12FrameContext frameContext)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_preparedFrame.TryGetTarget(out var preparedFrame) && ReferenceEquals(preparedFrame, frame))
            return;

        if (_texture is null || _width != frame.Width || _height != frame.Height)
        {
            if (_texture is not null)
                frameContext.RetireResource(_texture);

            _texture = _uploader.UploadRgbaTexture(
                _commandList,
                frame.Width,
                frame.Height,
                frame.Rgba,
                frameContext.TransientResources);
            _uploader.CreateShaderResourceView(_texture, _cpuHandle);
            _textureState = ResourceStates.PixelShaderResource;
            _width = frame.Width;
            _height = frame.Height;
        }
        else
        {
            _textureState = _uploader.UpdateRgbaTexture(
                _commandList,
                _texture,
                frame.Width,
                frame.Height,
                frame.Rgba,
                _textureState,
                frameContext.TransientResources);
        }

        _preparedFrame.SetTarget(frame);
    }

    public unsafe void Record(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState,
        int renderWidth,
        int renderHeight,
        float paperWhiteNits)
    {
        if (_texture is null || _textureState != ResourceStates.PixelShaderResource)
            return;

        var scale = Math.Min(renderWidth / (float)_width, renderHeight / (float)_height);
        var drawWidth = _width * scale;
        var drawHeight = _height * scale;
        var drawX = (renderWidth - drawWidth) * 0.5f;
        var drawY = (renderHeight - drawHeight) * 0.5f;

        Record(
            rootSignature,
            pipelineState,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            new Vector4(drawX, drawY, drawWidth, drawHeight));
    }

    public unsafe void Record(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState,
        int renderWidth,
        int renderHeight,
        float paperWhiteNits,
        Vector4 destinationRectangle)
    {
        if (_texture is null || _textureState != ResourceStates.PixelShaderResource)
            return;

        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        var values = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        _constants.Write(
            values,
            new WorldQuadShaderConstants(
                destinationRectangle,
                new Vector2(renderWidth, renderHeight),
                AmbientColour: Vector3.One,
                IsPremultipliedAlpha: false,
                PaperWhiteNits: paperWhiteNits));
        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            values,
            0);
        _commandList.SetGraphicsRootDescriptorTable(WorldQuadShaderLayout.TextureRootParameter, _gpuHandle);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _preparedFrame.SetTarget(null!);
    }
}
