using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.World.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Draws debug tile plans only when requested, without sector-sized render targets.</summary>
internal sealed class Dx12TerrainDebugPass(ID3D12Device device, Dx12TextureUploader uploader) : IDisposable
{
    private readonly Dictionary<TextureAsset, GuideTexture> _guides = new(ReferenceEqualityComparer.Instance);
    private readonly List<GpuTerrainTileInstance> _instances = [];
    private readonly List<GuideBatch> _batches = [];
    private Dx12TerrainDebugPipeline? _pipeline;
    private ID3D12DescriptorHeap? _srvHeap;
    private bool _hdrOutput;

    public void SetHdrOutput(bool enabled) => _hdrOutput = enabled;

    public void RecordSectors(
        ID3D12GraphicsCommandList commands,
        IReadOnlyList<TerrainSectorComposition> compositions,
        Dx12SectorTextureCache cache,
        TerrainDebugLayer layer,
        WorldScreenTransform transform,
        Dx12FrameContext frame,
        Format targetFormat,
        float paperWhiteNits,
        int width,
        int height)
    {
        foreach (var composition in compositions)
        {
            if (!cache.TryGet(composition.Coord, out _))
                continue;
            var topology = layer == TerrainDebugLayer.Topology;
            Record(commands, composition,
                topology ? composition.TerrainTopologyDebugTiles : composition.StairsDebugTiles,
                topology ? composition.TerrainTopologyDebugOffsetX : composition.StairsDebugOffsetX,
                topology ? composition.TerrainTopologyDebugOffsetY : composition.StairsDebugOffsetY,
                transform, frame, targetFormat, paperWhiteNits, width, height);
        }
    }

    public unsafe void Record(
        ID3D12GraphicsCommandList commands,
        TerrainSectorComposition composition,
        IReadOnlyList<TerrainCompositionTile> tiles,
        int offsetX,
        int offsetY,
        WorldScreenTransform transform,
        Dx12FrameContext frame,
        Format targetFormat,
        float paperWhiteNits,
        int width,
        int height)
    {
        _instances.Clear();
        _batches.Clear();
        foreach (var tile in tiles)
        {
            var position = transform.ToScreen(
                composition.IsoX + offsetX + tile.ScreenX,
                composition.IsoY + offsetY + tile.ScreenY);
            var elevation = tile.Surface.VisualElevation;
            var minimumElevation = MathF.Min(MathF.Min(elevation.SouthWest, elevation.NorthWest),
                MathF.Min(elevation.NorthEast, elevation.SouthEast));
            var maximumElevation = MathF.Max(MathF.Max(elevation.SouthWest, elevation.NorthWest),
                MathF.Max(elevation.NorthEast, elevation.SouthEast));
            if (position.X >= width || position.X + transform.Scale(96) <= 0 ||
                position.Y - transform.Scale(maximumElevation) >= height ||
                position.Y + transform.Scale(48 - minimumElevation) <= 0)
                continue;

            var source = tile.Primary.Texture;
            if (_batches.Count == 0 || !ReferenceEquals(_batches[^1].Texture, source))
                _batches.Add(new GuideBatch(source, _instances.Count, 1));
            else
                _batches[^1] = _batches[^1] with { Count = _batches[^1].Count + 1 };
            _instances.Add(new GpuTerrainTileInstance(position.X, position.Y,
                tile.Primary.SourceX, tile.Primary.SourceY, 0, 0, 0, 0, 0, tile.Surface));
        }
        if (_instances.Count == 0)
            return;

        _pipeline ??= new Dx12TerrainDebugPipeline(device, targetFormat);
        // Only the four tiny generated guides are uploaded. Append-only slots remain safe
        // while earlier frames are reading this heap.
        _srvHeap ??= device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            4, DescriptorHeapFlags.ShaderVisible, 0));
        foreach (var batch in _batches)
            EnsureGuide(commands, batch.Texture, frame);

        var buffer = uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_instances)));
        frame.RetireResource(buffer);
        commands.SetDescriptorHeaps(1, [_srvHeap]);
        commands.SetGraphicsRootSignature(_pipeline.RootSignature);
        commands.SetPipelineState(_pipeline.Pipeline);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        var constants = stackalloc float[4] { width, height, transform.Zoom, _hdrOutput ? paperWhiteNits : 0 };
        commands.SetGraphicsRoot32BitConstants(0, 4, constants, 0);
        var stride = Marshal.SizeOf<GpuTerrainTileInstance>();
        foreach (var batch in _batches)
        {
            commands.SetGraphicsRootDescriptorTable(2, _guides[batch.Texture].Handle);
            commands.SetGraphicsRootShaderResourceView(1, buffer.GPUVirtualAddress + (ulong)(batch.First * stride));
            commands.DrawInstanced(6, (uint)batch.Count, 0, 0);
        }
    }

    private void EnsureGuide(ID3D12GraphicsCommandList commands, TextureAsset source, Dx12FrameContext frame)
    {
        if (_guides.ContainsKey(source))
            return;
        if (_guides.Count == 4)
            throw new InvalidOperationException("The terrain debug guide heap is full.");
        var resource = uploader.UploadRgbaTexture(commands, source.Width, source.Height,
            source.Rgba8, frame.TransientResources);
        var descriptorSize = (int)device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        var offset = _guides.Count * descriptorSize;
        uploader.CreateShaderResourceView(resource, _srvHeap!.GetCPUDescriptorHandleForHeapStart() + offset);
        _guides.Add(source, new GuideTexture(resource, _srvHeap.GetGPUDescriptorHandleForHeapStart() + offset));
        EngineLog.WriteLine($"Terrain debug guide loaded: {source.Name}; {source.Width}x{source.Height}. No sector debug target allocated.");
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
    }

    public void Dispose()
    {
        DisposePipeline();
        foreach (var guide in _guides.Values)
            guide.Resource.Dispose();
        _guides.Clear();
        _srvHeap?.Dispose();
    }

    private sealed record GuideTexture(ID3D12Resource Resource, GpuDescriptorHandle Handle);
    private readonly record struct GuideBatch(TextureAsset Texture, int First, int Count);
}

internal enum TerrainDebugLayer { Stairs, Topology }
