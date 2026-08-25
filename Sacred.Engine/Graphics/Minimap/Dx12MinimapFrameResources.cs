using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Minimap;

/// <summary>Owns minimap textures whose descriptors are safe to update for one retired frame.</summary>
internal sealed class Dx12MinimapFrameResources(int firstSrvSlot) : IDisposable
{
    public const int MapTextureCount = 49;
    private const int BackgroundSlotOffset = MapTextureCount;
    private const int MarkerSlotOffset = BackgroundSlotOffset + 1;
    private const int LabelSlotOffset = MarkerSlotOffset + 1;
    public const int DescriptorCount = LabelSlotOffset + 1;

    private static readonly byte[] BackgroundPixel = [0, 0, 0, 255];
    private static readonly byte[] MarkerPixel = [245, 226, 106, 255];

    private readonly int _firstSrvSlot = firstSrvSlot;
    private ID3D12Resource? _background;
    private ID3D12Resource? _marker;
    private ID3D12Resource? _label;
    private string _labelText = string.Empty;

    public Dx12MinimapTextureSlot[] MapTextures { get; } = CreateMapSlots(firstSrvSlot);

    public void PrepareUiTextures(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        Func<int, CpuDescriptorHandle> cpuHandle,
        MinimapLabelRasterizer labelRasterizer,
        string difficultyDisplayName,
        string regionDisplayName,
        ICollection<ID3D12Resource> transientResources)
    {
        _background ??= Upload(
            commandList, uploader, cpuHandle(_firstSrvSlot + BackgroundSlotOffset), 1, 1,
            BackgroundPixel, transientResources);
        PrepareMarkerTexture(commandList, uploader, cpuHandle, transientResources);

        var labelText = string.Concat(difficultyDisplayName, "\n", regionDisplayName);
        if (_label is not null && string.Equals(_labelText, labelText, StringComparison.Ordinal))
            return;

        _label?.Dispose();
        _labelText = labelText;
        _label = Upload(
            commandList,
            uploader,
            cpuHandle(_firstSrvSlot + LabelSlotOffset),
            MinimapLabelRasterizer.Width,
            MinimapLabelRasterizer.Height,
            labelRasterizer.Rasterize(difficultyDisplayName, regionDisplayName),
            transientResources);
    }

    public void PrepareMarkerTexture(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        Func<int, CpuDescriptorHandle> cpuHandle,
        ICollection<ID3D12Resource> transientResources)
    {
        _marker ??= Upload(
            commandList,
            uploader,
            cpuHandle(_firstSrvSlot + MarkerSlotOffset),
            1,
            1,
            MarkerPixel,
            transientResources);
    }

    public void PrepareMapTexture(
        int index,
        SectorCoord coord,
        TextureAsset? asset,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        Func<int, CpuDescriptorHandle> cpuHandle,
        ICollection<ID3D12Resource> transientResources) =>
        MapTextures[index].Prepare(
            coord,
            asset,
            commandList,
            uploader,
            cpuHandle(MapTextures[index].SrvSlot),
            transientResources);

    public GpuDescriptorHandle BackgroundGpuHandle(Func<int, GpuDescriptorHandle> gpuHandle) =>
        gpuHandle(_firstSrvSlot + BackgroundSlotOffset);

    public GpuDescriptorHandle MarkerGpuHandle(Func<int, GpuDescriptorHandle> gpuHandle) =>
        gpuHandle(_firstSrvSlot + MarkerSlotOffset);

    public GpuDescriptorHandle LabelGpuHandle(Func<int, GpuDescriptorHandle> gpuHandle) =>
        gpuHandle(_firstSrvSlot + LabelSlotOffset);

    private static Dx12MinimapTextureSlot[] CreateMapSlots(int firstSrvSlot)
    {
        var slots = new Dx12MinimapTextureSlot[MapTextureCount];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = new Dx12MinimapTextureSlot(firstSrvSlot + i);
        return slots;
    }

    private static ID3D12Resource Upload(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        CpuDescriptorHandle cpuHandle,
        int width,
        int height,
        byte[] rgba,
        ICollection<ID3D12Resource> transientResources)
    {
        var texture = uploader.UploadRgbaTexture(
            commandList, width, height, rgba, transientResources);
        uploader.CreateShaderResourceView(texture, cpuHandle);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in MapTextures)
            texture.Dispose();
        _background?.Dispose();
        _marker?.Dispose();
        _label?.Dispose();
    }
}

internal sealed class Dx12MinimapTextureSlot(int srvSlot) : IDisposable
{
    public int SrvSlot { get; } = srvSlot;
    public SectorCoord? Coord { get; private set; }
    public ID3D12Resource? Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void Prepare(
        SectorCoord coord,
        TextureAsset? asset,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        CpuDescriptorHandle cpuHandle,
        ICollection<ID3D12Resource> transientResources)
    {
        if (asset is null)
        {
            if (Coord != coord)
                Clear();
            return;
        }

        if (Coord == coord && Texture is not null)
            return;

        Clear();
        Texture = uploader.UploadRgbaTexture(
            commandList,
            asset.Width,
            asset.Height,
            asset.Rgba8,
            transientResources);
        uploader.CreateShaderResourceView(Texture, cpuHandle);
        Coord = coord;
        Width = asset.Width;
        Height = asset.Height;
    }

    private void Clear()
    {
        Texture?.Dispose();
        Texture = null;
        Coord = null;
        Width = 0;
        Height = 0;
    }

    public void Dispose() => Clear();
}
