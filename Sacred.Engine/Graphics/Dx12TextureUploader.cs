using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

public sealed class Dx12TextureUploader
{
    private const Format Rgba8Format = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;

    public Dx12TextureUploader(ID3D12Device device) => _device = device;

    public void CreateShaderResourceView(ID3D12Resource texture, CpuDescriptorHandle descriptor) =>
        _device.CreateShaderResourceView(texture, null, descriptor);

    public unsafe ID3D12Resource CreateUploadBuffer(ReadOnlySpan<byte> bytes)
    {
        var description = new ResourceDescription(
            ResourceDimension.Buffer,
            0,
            (ulong)bytes.Length,
            1,
            1,
            1,
            Format.Unknown,
            1,
            0,
            TextureLayout.RowMajor,
            ResourceFlags.None);

        var resource = CreateCommittedResource(HeapType.Upload, description, ResourceStates.GenericRead);

        void* mapped;
        resource.Map(0, null, &mapped).CheckError();
        try
        {
            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, mapped, bytes.Length, bytes.Length);
        }
        finally
        {
            resource.Unmap(0, null);
        }

        return resource;
    }

    public static unsafe void UpdateUploadBuffer(ID3D12Resource resource, ReadOnlySpan<byte> bytes)
    {
        if ((ulong)bytes.Length > resource.Description.Width)
            throw new ArgumentException("The source data is larger than the upload buffer.", nameof(bytes));

        void* mapped;
        resource.Map(0, null, &mapped).CheckError();
        try
        {
            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, mapped, checked((long)resource.Description.Width), bytes.Length);
        }
        finally
        {
            resource.Unmap(0, null);
        }
    }

    public ID3D12Resource UploadRgbaTexture(
        ID3D12GraphicsCommandList commandList,
        int width,
        int height,
        byte[] rgba,
        ICollection<ID3D12Resource> retainedUploadResources)
    {
        var texture = CreateTexture2D(width, height, Rgba8Format, ResourceStates.CopyDest);
        try
        {
            UpdateRgbaTexture(
                commandList,
                texture,
                width,
                height,
                rgba,
                ResourceStates.CopyDest,
                retainedUploadResources);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    public ResourceStates UpdateRgbaTexture(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource texture,
        int width,
        int height,
        byte[] rgba,
        ResourceStates currentState,
        ICollection<ID3D12Resource> retainedUploadResources)
    {
        var upload = CreateRgbaUploadBuffer(width, height, rgba);

        if (currentState != ResourceStates.CopyDest)
            Transition(commandList, texture, currentState, ResourceStates.CopyDest);

        CopyUploadToTexture(commandList, upload, texture, width, height);
        Transition(commandList, texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

        retainedUploadResources.Add(upload);
        return ResourceStates.PixelShaderResource;
    }

    public ID3D12Resource UploadRgbaTextureAndWait(
        ID3D12CommandQueue commandQueue,
        ID3D12CommandAllocator commandAllocator,
        ID3D12GraphicsCommandList commandList,
        ID3D12Fence fence,
        nint fenceEvent,
        ref ulong fenceValue,
        int width,
        int height,
        byte[] rgba)
    {
        ID3D12Resource? texture = null;
        ID3D12Resource? upload = null;
        try
        {
            texture = CreateTexture2D(width, height, Rgba8Format, ResourceStates.CopyDest);
            upload = CreateRgbaUploadBuffer(width, height, rgba);

            commandAllocator.Reset();
            commandList.Reset(commandAllocator, null);
            CopyUploadToTexture(commandList, upload, texture, width, height);
            Transition(commandList, texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            commandList.Close();

            commandQueue.ExecuteCommandLists([commandList]);
            fenceValue++;
            commandQueue.Signal(fence, fenceValue).CheckError();
            if (fence.CompletedValue < fenceValue)
            {
                fence.SetEventOnCompletion(fenceValue, fenceEvent).CheckError();
                Kernel32.WaitForSingleObject(fenceEvent, uint.MaxValue);
            }

            upload.Dispose();
            upload = null;
            return texture;
        }
        catch
        {
            upload?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Submits both terrain textures without waiting for the GPU. The returned upload owns every
    /// object that must remain alive until its fence has completed.
    /// </summary>
    public Dx12SectorTextureUpload SubmitSectorTextures(
        ID3D12CommandQueue commandQueue,
        ID3D12Fence fence,
        ref ulong fenceValue,
        int width,
        int height,
        byte[] baseRgba,
        byte[] liquidCoverRgba)
    {
        ID3D12CommandAllocator? commandAllocator = null;
        ID3D12GraphicsCommandList? commandList = null;
        ID3D12Resource? baseTexture = null;
        ID3D12Resource? liquidCoverTexture = null;
        ID3D12Resource? baseUpload = null;
        ID3D12Resource? liquidCoverUpload = null;
        try
        {
            commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
            commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, commandAllocator, null);
            baseTexture = CreateTexture2D(width, height, Rgba8Format, ResourceStates.CopyDest);
            liquidCoverTexture = CreateTexture2D(width, height, Rgba8Format, ResourceStates.CopyDest);
            baseUpload = CreateRgbaUploadBuffer(width, height, baseRgba);
            liquidCoverUpload = CreateRgbaUploadBuffer(width, height, liquidCoverRgba);

            CopyUploadToTexture(commandList, baseUpload, baseTexture, width, height);
            Transition(commandList, baseTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            CopyUploadToTexture(commandList, liquidCoverUpload, liquidCoverTexture, width, height);
            Transition(commandList, liquidCoverTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            commandList.Close();
            commandQueue.ExecuteCommandLists([commandList]);
            fenceValue++;
            commandQueue.Signal(fence, fenceValue).CheckError();

            return new Dx12SectorTextureUpload(
                baseTexture,
                liquidCoverTexture,
                baseUpload,
                liquidCoverUpload,
                commandAllocator,
                commandList,
                fenceValue);
        }
        catch
        {
            liquidCoverUpload?.Dispose();
            baseUpload?.Dispose();
            liquidCoverTexture?.Dispose();
            baseTexture?.Dispose();
            commandList?.Dispose();
            commandAllocator?.Dispose();
            throw;
        }
    }

    private ID3D12Resource CreateCommittedResource(HeapType heapType, ResourceDescription description, ResourceStates initialState)
    {
        var heapProperties = new HeapProperties(heapType, 0, 0);
        return _device.CreateCommittedResource(heapProperties, HeapFlags.None, description, initialState, null);
    }

    private ID3D12Resource CreateTexture2D(int width, int height, Format format, ResourceStates initialState)
    {
        var textureDescription = new ResourceDescription(
            ResourceDimension.Texture2D,
            0,
            (ulong)width,
            (uint)height,
            1,
            1,
            format,
            1,
            0,
            TextureLayout.Unknown,
            ResourceFlags.None);

        return CreateCommittedResource(HeapType.Default, textureDescription, initialState);
    }

    private unsafe ID3D12Resource CreateRgbaUploadBuffer(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");

        var rowBytes = width * 4;
        var requiredBytes = rowBytes * height;
        if (rgba.Length < requiredBytes)
            throw new ArgumentException($"RGBA buffer is too small for {width}x{height} texture.", nameof(rgba));

        var rowPitch = Align(rowBytes, 256);
        var uploadSize = (ulong)(rowPitch * height);
        var uploadDescription = new ResourceDescription(
            ResourceDimension.Buffer,
            0,
            uploadSize,
            1,
            1,
            1,
            Format.Unknown,
            1,
            0,
            TextureLayout.RowMajor,
            ResourceFlags.None);

        var upload = CreateCommittedResource(HeapType.Upload, uploadDescription, ResourceStates.GenericRead);

        void* mapped;
        upload.Map(0, null, &mapped).CheckError();
        try
        {
            for (var y = 0; y < height; y++)
            {
                var sourceOffset = y * rowBytes;
                var destination = IntPtr.Add((nint)mapped, y * rowPitch);
                Marshal.Copy(rgba, sourceOffset, destination, rowBytes);
            }
        }
        finally
        {
            upload.Unmap(0, null);
        }

        return upload;
    }

    private static void CopyUploadToTexture(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource upload,
        ID3D12Resource texture,
        int width,
        int height)
    {
        var rowPitch = Align(width * 4, 256);
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(Rgba8Format, (uint)width, (uint)height, 1, (uint)rowPitch)
        };

        var source = new TextureCopyLocation(upload, footprint);
        var destination = new TextureCopyLocation(texture, 0);
        commandList.CopyTextureRegion(destination, 0, 0, 0, source, null);
    }

    public static void Transition(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource resource,
        ResourceStates before,
        ResourceStates after)
    {
        var barrier = ResourceBarrier.BarrierTransition(resource, before, after, uint.MaxValue, ResourceBarrierFlags.None);
        commandList.ResourceBarrier([barrier]);
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}

public sealed class Dx12SectorTextureUpload : IDisposable
{
    private ID3D12Resource? _baseUpload;
    private ID3D12Resource? _liquidCoverUpload;
    private ID3D12CommandAllocator? _commandAllocator;
    private ID3D12GraphicsCommandList? _commandList;

    internal Dx12SectorTextureUpload(
        ID3D12Resource baseTexture,
        ID3D12Resource liquidCoverTexture,
        ID3D12Resource baseUpload,
        ID3D12Resource liquidCoverUpload,
        ID3D12CommandAllocator commandAllocator,
        ID3D12GraphicsCommandList commandList,
        ulong fenceValue)
    {
        BaseTexture = baseTexture;
        LiquidCoverTexture = liquidCoverTexture;
        _baseUpload = baseUpload;
        _liquidCoverUpload = liquidCoverUpload;
        _commandAllocator = commandAllocator;
        _commandList = commandList;
        FenceValue = fenceValue;
    }

    public ID3D12Resource BaseTexture { get; }
    public ID3D12Resource LiquidCoverTexture { get; }
    public ulong FenceValue { get; }

    public void ReleaseCompletedUploadResources()
    {
        _liquidCoverUpload?.Dispose();
        _liquidCoverUpload = null;
        _baseUpload?.Dispose();
        _baseUpload = null;
        _commandList?.Dispose();
        _commandList = null;
        _commandAllocator?.Dispose();
        _commandAllocator = null;
    }

    public void Dispose()
    {
        ReleaseCompletedUploadResources();
        BaseTexture.Dispose();
        LiquidCoverTexture.Dispose();
    }
}
