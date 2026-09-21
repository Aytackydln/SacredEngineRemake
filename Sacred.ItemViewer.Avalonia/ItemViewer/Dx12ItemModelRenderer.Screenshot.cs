using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    public unsafe void SaveScreenshot(string path)
    {
        // Render a fresh frame directly; capture never depends on focus or Present.
        ObjectDisposedException.ThrowIf(_disposed, this);
        WaitForGpu();
        ResizeIfNeeded();
        var rowPitch = (_renderWidth * 4 + 255) & ~255;
        var description = new ResourceDescription(ResourceDimension.Buffer, 0,
            (ulong)(rowPitch * _renderHeight), 1, 1, 1, Format.Unknown, 1, 0,
            TextureLayout.RowMajor, ResourceFlags.None);
        using var readback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback, 0, 0),
            HeapFlags.None, description, ResourceStates.CopyDest, null);
        _commandAllocator.Reset();
        _commandList.Reset(_commandAllocator, _pipelineState);
        RecordFrame();
        var backBuffer = _backBuffers[_swapChain.CurrentBackBufferIndex];
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.CopySource);
        var footprint = new PlacedSubresourceFootPrint
        {
            Footprint = new SubresourceFootPrint(BackBufferFormat, (uint)_renderWidth, (uint)_renderHeight, 1, (uint)rowPitch)
        };
        _commandList.CopyTextureRegion(new TextureCopyLocation(readback, footprint), 0, 0, 0,
            new TextureCopyLocation(backBuffer, 0), null);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopySource, ResourceStates.Present);
        _commandList.Close();
        _commandQueue.ExecuteCommandLists([_commandList]);
        SignalFrameFence();
        WaitForGpu();
        void* pixels;
        readback.Map(0, null, &pixels).CheckError();
        try
        {
            using var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Opaque, (nint)pixels,
                new PixelSize(_renderWidth, _renderHeight), new Vector(96, 96), rowPitch);
            bitmap.Save(path);
        }
        finally
        {
            readback.Unmap(0, null);
        }
        Console.WriteLine($"[Inventory] Screenshot saved: {path}");
    }
}
