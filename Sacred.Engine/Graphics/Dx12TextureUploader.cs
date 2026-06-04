using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

public sealed unsafe class Dx12TextureUploader(ID3D12Device device)
{
    private const Format Rgba8Format = Format.R8G8B8A8_UNorm;

    public void CreateShaderResourceView(ID3D12Resource texture, CpuDescriptorHandle descriptor) =>
        device.CreateShaderResourceView(texture, null, descriptor);

    public ID3D12Resource CreateUploadBuffer(ReadOnlySpan<byte> bytes)
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
        retainedUploadResources.Add(upload);

        if (currentState != ResourceStates.CopyDest)
            Transition(commandList, texture, currentState, ResourceStates.CopyDest);

        CopyUploadToTexture(commandList, upload, texture, width, height);
        Transition(commandList, texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
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

    private ID3D12Resource CreateCommittedResource(HeapType heapType, ResourceDescription description, ResourceStates initialState)
    {
        var heapProperties = new HeapProperties(heapType, 0, 0);
        return device.CreateCommittedResource(heapProperties, HeapFlags.None, description, initialState, null);
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

    private ID3D12Resource CreateRgbaUploadBuffer(int width, int height, byte[] rgba)
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
