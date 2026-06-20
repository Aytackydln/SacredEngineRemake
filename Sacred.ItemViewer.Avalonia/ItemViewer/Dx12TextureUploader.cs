using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed class Dx12TextureUploader : IDisposable
{
    private static readonly Format TextureFormat = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _fence;
    private readonly SemaphoreSlim _uploadLock = new(1, 1);
    private nint _fenceEvent;
    private ulong _fenceValue;
    private bool _disposed;

    public Dx12TextureUploader(ID3D12Device device)
    {
        _device = device;
        _queue = device.CreateCommandQueue(CommandListType.Direct);
        _allocator = device.CreateCommandAllocator(CommandListType.Direct);
        _commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _allocator, null);
        _commandList.Close();
        _fence = device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Win32Native.CreateEvent(0, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create the texture upload fence event.");
    }

    public async Task<ID3D12Resource> UploadAsync(
        int width,
        int height,
        byte[] rgba,
        CancellationToken cancellationToken = default)
    {
        await _uploadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(
                () => UploadAndWait(width, height, rgba, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _uploadLock.Release();
        }
    }

    private ID3D12Resource UploadAndWait(
        int width,
        int height,
        byte[] rgba,
        CancellationToken cancellationToken)
    {
        ID3D12Resource? texture = null;
        ID3D12Resource? upload = null;
        try
        {
            texture = CreateTexture(width, height);
            upload = CreateUploadBuffer(width, height, rgba);

            _allocator.Reset();
            _commandList.Reset(_allocator, null);
            CopyToTexture(upload, texture, width, height);
            var barrier = ResourceBarrier.BarrierTransition(
                texture,
                ResourceStates.CopyDest,
                ResourceStates.PixelShaderResource);
            _commandList.ResourceBarrier([barrier]);
            _commandList.Close();
            _queue.ExecuteCommandLists([_commandList]);

            var fenceValue = ++_fenceValue;
            _queue.Signal(_fence, fenceValue).CheckError();
            if (_fence.CompletedValue < fenceValue)
            {
                _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
                Win32Native.WaitForSingleObject(_fenceEvent, uint.MaxValue);
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    private ID3D12Resource CreateTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");

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
            ResourceFlags.None);
        return _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            description,
            ResourceStates.CopyDest);
    }

    private unsafe ID3D12Resource CreateUploadBuffer(int width, int height, byte[] rgba)
    {
        var rowBytes = checked(width * 4);
        var requiredBytes = checked(rowBytes * height);
        if (rgba.Length < requiredBytes)
            throw new ArgumentException($"RGBA buffer is too small for {width}x{height} texture.", nameof(rgba));

        var rowPitch = Align(rowBytes, 256);
        var description = new ResourceDescription(
            ResourceDimension.Buffer,
            0,
            (ulong)checked(rowPitch * height),
            1,
            1,
            1,
            Format.Unknown,
            1,
            0,
            TextureLayout.RowMajor,
            ResourceFlags.None);
        var upload = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            description,
            ResourceStates.GenericRead);

        void* mapped;
        upload.Map(0, null, &mapped).CheckError();
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(rgba, y * rowBytes, IntPtr.Add((nint)mapped, y * rowPitch), rowBytes);
        }
        finally
        {
            upload.Unmap(0, null);
        }

        return upload;
    }

    private void CopyToTexture(ID3D12Resource upload, ID3D12Resource texture, int width, int height)
    {
        var footprint = new PlacedSubresourceFootPrint
        {
            Footprint = new SubresourceFootPrint(
                TextureFormat,
                (uint)width,
                (uint)height,
                1,
                (uint)Align(width * 4, 256))
        };
        _commandList.CopyTextureRegion(
            new TextureCopyLocation(texture, 0),
            0,
            0,
            0,
            new TextureCopyLocation(upload, footprint),
            null);
    }

    public void Dispose()
    {
        _uploadLock.Wait();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            _fence.Dispose();
            _commandList.Dispose();
            _allocator.Dispose();
            _queue.Dispose();
            if (_fenceEvent != 0)
            {
                Win32Native.CloseHandle(_fenceEvent);
                _fenceEvent = 0;
            }
        }
        finally
        {
            _uploadLock.Release();
            _uploadLock.Dispose();
        }
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}
