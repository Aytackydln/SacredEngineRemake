using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Minimap;

/// <summary>Draws source minimap sector textures as independent, clipped GPU quads.</summary>
internal sealed class Dx12MinimapPass : IDisposable
{
    private const int SectorRadius = 3;
    private const float MapBrightness = 0.72f;

    public const int DescriptorsPerFrame = Dx12MinimapFrameResources.DescriptorCount;

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12DescriptorHeap _srvHeap;
    private readonly int _descriptorSize;
    private readonly AssetManager _assets;
    private readonly Func<SectorCoord, string?> _resolveTextureName;
    private readonly MinimapLabelRasterizer _labelRasterizer;
    private readonly Dictionary<SectorCoord, Task<TextureAsset?>> _textureLoads = [];
    private readonly HashSet<SectorCoord> _requestedCoords = [];
    private readonly List<SectorCoord> _staleLoads = new(Dx12MinimapFrameResources.MapTextureCount);
    private readonly Dx12MinimapFrameResources[] _frames;
    private readonly WorldQuadShaderConstantsUpdater _constants = new();

    private Dx12MinimapFrameResources? _preparedFrame;
    private SectorCoord _preparedCenterSector;
    private Vector2 _preparedPlayerOffsetInTiles;
    private bool _disposed;

    public Dx12MinimapPass(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int firstSrvSlot,
        int frameCount,
        AssetManager assets,
        Func<SectorCoord, string?> resolveTextureName,
        string gameDirectory)
    {
        _commandList = commandList;
        _uploader = uploader;
        _srvHeap = srvHeap;
        _descriptorSize = descriptorSize;
        _assets = assets;
        _resolveTextureName = resolveTextureName;
        _labelRasterizer = new MinimapLabelRasterizer(gameDirectory);
        _frames = new Dx12MinimapFrameResources[frameCount];
        for (var i = 0; i < frameCount; i++)
            _frames[i] = new Dx12MinimapFrameResources(firstSrvSlot + i * DescriptorsPerFrame);
    }

    public void Prepare(
        Vector2 playerWorldPosition,
        string difficultyDisplayName,
        Dx12FrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = _frames[frameContext.Index];
        frame.PrepareUiTextures(
            _commandList,
            _uploader,
            CpuHandle,
            _labelRasterizer,
            difficultyDisplayName,
            frameContext.TransientResources);

        var centerSector = new SectorCoord(
            (int)MathF.Floor(playerWorldPosition.X / Sector.TileCount),
            (int)MathF.Floor(playerWorldPosition.Y / Sector.TileCount));
        _preparedCenterSector = centerSector;
        _preparedPlayerOffsetInTiles = playerWorldPosition - new Vector2(
            (centerSector.X + 0.5f) * Sector.TileCount,
            (centerSector.Y + 0.5f) * Sector.TileCount);

        _requestedCoords.Clear();
        var slotIndex = 0;
        for (var deltaY = -SectorRadius; deltaY <= SectorRadius; deltaY++)
        for (var deltaX = -SectorRadius; deltaX <= SectorRadius; deltaX++)
        {
            var coord = new SectorCoord(
                centerSector.X + deltaX,
                centerSector.Y + deltaY);
            _requestedCoords.Add(coord);
            var load = GetOrStartLoad(coord);
            var texture = load is { IsCompletedSuccessfully: true } ? load.Result : null;
            frame.PrepareMapTexture(
                slotIndex,
                coord,
                texture,
                _commandList,
                _uploader,
                CpuHandle,
                frameContext.TransientResources);
            slotIndex++;
        }

        RemoveStaleCompletedLoads();
        _preparedFrame = frame;
    }

    public void PrepareTargetMarker(Dx12FrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = _frames[frameContext.Index];
        frame.PrepareMarkerTexture(
            _commandList,
            _uploader,
            CpuHandle,
            frameContext.TransientResources);
        _preparedFrame = frame;
    }

    public unsafe void Record(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState,
        int renderWidth,
        int renderHeight,
        float paperWhiteNits)
    {
        if (_preparedFrame is not { } frame)
            return;

        var panel = MinimapPanelLayout.Calculate(renderWidth, renderHeight);
        _commandList.RSSetScissorRects(new RawRect(
            (int)MathF.Floor(panel.X),
            (int)MathF.Floor(panel.Y),
            (int)MathF.Ceiling(panel.X + panel.Width),
            (int)MathF.Ceiling(panel.Y + panel.Height)));
        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var values = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        RecordQuad(
            frame.BackgroundGpuHandle(GpuHandle),
            new Vector4(panel.X, panel.Y, panel.Width, panel.Height),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);

        for (var index = 0; index < frame.MapTextures.Length; index++)
        {
            var slot = frame.MapTextures[index];
            if (slot.Texture is null || slot.Coord is not { } coord)
                continue;

            var deltaX = coord.X - _preparedCenterSector.X;
            var deltaY = coord.Y - _preparedCenterSector.Y;

            // The minimap files form the same staggered isometric lattice as the
            // world: X and Y neighbors are one image sideways and half an image
            // vertically; (1,1) neighbors form the straight vertical columns.
            var playerOffsetX =
                (_preparedPlayerOffsetInTiles.X - _preparedPlayerOffsetInTiles.Y) *
                slot.Width / Sector.TileCount;
            var playerOffsetY =
                (_preparedPlayerOffsetInTiles.X + _preparedPlayerOffsetInTiles.Y) *
                slot.Height / (Sector.TileCount * 2.0f);
            // A minimap texture's horizontal world anchor is its right edge, not
            // its center. Account for that half-sector offset before centering
            // the player's projected position.
            var drawX = panel.CenterX - slot.Width +
                        (deltaX - deltaY) * slot.Width - playerOffsetX;
            var drawY = panel.CenterY - slot.Height * 0.5f +
                        (deltaX + deltaY) * slot.Height * 0.5f - playerOffsetY;
            RecordQuad(
                GpuHandle(slot.SrvSlot),
                new Vector4(drawX, drawY, slot.Width, slot.Height),
                MapBrightness,
                renderWidth,
                renderHeight,
                paperWhiteNits,
                values);
        }

        RecordQuad(
            frame.LabelGpuHandle(GpuHandle),
            new Vector4(panel.X + 12.0f, panel.Y + 8.0f, MinimapLabelRasterizer.Width, MinimapLabelRasterizer.Height),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);

        // A simple high-contrast player marker; map icons can join this pass later.
        RecordQuad(
            frame.MarkerGpuHandle(GpuHandle),
            new Vector4(panel.CenterX - 9.0f, panel.CenterY - 1.5f, 18.0f, 3.0f),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);
        RecordQuad(
            frame.MarkerGpuHandle(GpuHandle),
            new Vector4(panel.CenterX - 1.5f, panel.CenterY - 9.0f, 3.0f, 18.0f),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);

        _commandList.RSSetScissorRects(new RawRect(0, 0, renderWidth, renderHeight));
    }

    public unsafe void RecordTargetMarker(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState,
        Vector2 screenPosition,
        int renderWidth,
        int renderHeight,
        float paperWhiteNits)
    {
        if (_preparedFrame is not { } frame)
            return;

        _commandList.RSSetScissorRects(new RawRect(0, 0, renderWidth, renderHeight));
        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var values = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        RecordQuad(
            frame.MarkerGpuHandle(GpuHandle),
            new Vector4(screenPosition.X - 12.0f, screenPosition.Y - 1.5f, 24.0f, 3.0f),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);
        RecordQuad(
            frame.MarkerGpuHandle(GpuHandle),
            new Vector4(screenPosition.X - 1.5f, screenPosition.Y - 12.0f, 3.0f, 24.0f),
            1.0f,
            renderWidth,
            renderHeight,
            paperWhiteNits,
            values);
    }

    private unsafe void RecordQuad(
        GpuDescriptorHandle texture,
        Vector4 rect,
        float brightness,
        int renderWidth,
        int renderHeight,
        float paperWhiteNits,
        float* values)
    {
        _constants.Write(
            values,
            new WorldQuadShaderConstants(
                rect,
                new Vector2(renderWidth, renderHeight),
                brightness,
                IsPremultipliedAlpha: false,
                paperWhiteNits));
        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            values,
            0);
        _commandList.SetGraphicsRootDescriptorTable(WorldQuadShaderLayout.TextureRootParameter, texture);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    private Task<TextureAsset?>? GetOrStartLoad(SectorCoord coord)
    {
        if (_textureLoads.TryGetValue(coord, out var existing))
            return existing;

        var textureName = _resolveTextureName(coord);
        if (textureName is null)
            return null;

        var load = LoadTextureAsync(coord, textureName);
        _textureLoads.Add(coord, load);
        return load;
    }

    private async Task<TextureAsset?> LoadTextureAsync(SectorCoord coord, string textureName)
    {
        try
        {
            var texture = await _assets.LoadTextureAsync(textureName).ConfigureAwait(false);
        EngineLog.WriteLine($"Minimap texture loaded: {textureName} ({texture.Width}x{texture.Height}).");
            return texture;
        }
        catch (Exception exception)
        {
        EngineLog.WriteLine($"Minimap texture unavailable for sector {coord.X},{coord.Y}: {exception.Message}");
            return null;
        }
    }

    private void RemoveStaleCompletedLoads()
    {
        _staleLoads.Clear();
        foreach (var pair in _textureLoads)
        {
            if (pair.Value.IsCompleted && !_requestedCoords.Contains(pair.Key))
                _staleLoads.Add(pair.Key);
        }

        foreach (var coord in _staleLoads)
            _textureLoads.Remove(coord);
    }

    private CpuDescriptorHandle CpuHandle(int slot) =>
        _srvHeap.GetCPUDescriptorHandleForHeapStart() + slot * _descriptorSize;

    private GpuDescriptorHandle GpuHandle(int slot) =>
        _srvHeap.GetGPUDescriptorHandleForHeapStart() + slot * _descriptorSize;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            Task.WhenAll(_textureLoads.Values).GetAwaiter().GetResult();
        }
        catch
        {
            // Individual loads already report failures and resolve to null.
        }

        foreach (var frame in _frames)
            frame.Dispose();
        _labelRasterizer.Dispose();
        _textureLoads.Clear();
        _preparedFrame = null;
    }

}
