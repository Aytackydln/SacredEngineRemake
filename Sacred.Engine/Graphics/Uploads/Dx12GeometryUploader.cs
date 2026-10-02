using System;
using System.Threading;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Uploads;

/// <summary>Copies immutable geometry on asset workers before publishing it to the renderer.</summary>
internal sealed class Dx12GeometryUploader : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12CommandQueue _queue;
    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12GraphicsCommandList _commands;
    private readonly ID3D12Fence _fence;
    private readonly AutoResetEvent _completed = new(false);
    private readonly object _sync = new();
    private ulong _fenceValue;

    public Dx12GeometryUploader(ID3D12Device device, Dx12TextureUploader uploader)
    {
        _device = device;
        _uploader = uploader;
        _queue = device.CreateCommandQueue(CommandListType.Copy);
        _allocator = device.CreateCommandAllocator(CommandListType.Copy);
        _commands = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Copy, _allocator, null);
        _commands.Close();
        _fence = device.CreateFence(0, FenceFlags.None);
    }

    public ID3D12Resource Upload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            throw new ArgumentException("Geometry buffer must not be empty.", nameof(bytes));

        // Asset preparations may run concurrently; the allocator and fence belong to one submission.
        lock (_sync)
        {
            var buffer = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                new ResourceDescription(ResourceDimension.Buffer, 0, (ulong)bytes.Length, 1, 1, 1,
                    Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None), ResourceStates.Common, null);
            try
            {
                using var staging = _uploader.CreateUploadBuffer(bytes);
                _allocator.Reset();
                _commands.Reset(_allocator, null);
                // COMMON promotes to COPY_DEST and decays to COMMON after the copy submission.
                // After the fence completes, graphics draws can promote it to vertex/index reads.
                _commands.CopyBufferRegion(buffer, 0, staging, 0, (ulong)bytes.Length);
                _commands.Close();
                _queue.ExecuteCommandLists([_commands]);
                var fenceValue = ++_fenceValue;
                _queue.Signal(_fence, fenceValue).CheckError();
                if (_fence.CompletedValue < fenceValue)
                {
                    _fence.SetEventOnCompletion(fenceValue, _completed.SafeWaitHandle.DangerousGetHandle()).CheckError();
                    _completed.WaitOne();
                }
                // Staging is released only after the GPU copy has finished.
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _commands.Dispose();
            _allocator.Dispose();
            _fence.Dispose();
            _queue.Dispose();
            _completed.Dispose();
        }
    }
}
