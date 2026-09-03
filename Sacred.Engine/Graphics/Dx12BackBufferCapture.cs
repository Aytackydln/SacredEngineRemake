using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Records and reads back a copy of a rendered D3D12 back buffer.</summary>
internal sealed class Dx12BackBufferCapture : IDisposable
{
    private const int BytesPerPixel = 4;
    private const int TextureDataPitchAlignment = 256;

    private readonly ID3D12Resource _readbackBuffer;
    private readonly int _rowPitch;

    private Dx12BackBufferCapture(
        ID3D12Resource readbackBuffer,
        int width,
        int height,
        int rowPitch,
        Format format,
        ColorSpaceType colorSpace)
    {
        _readbackBuffer = readbackBuffer;
        Width = width;
        Height = height;
        _rowPitch = rowPitch;
        Format = format;
        ColorSpace = colorSpace;
    }

    public int Width { get; }
    public int Height { get; }
    public Format Format { get; }
    public ColorSpaceType ColorSpace { get; }

    public static Dx12BackBufferCapture Record(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource backBuffer,
        int width,
        int height,
        Format format,
        ColorSpaceType colorSpace)
    {
        var rowPitch = Align(width * BytesPerPixel, TextureDataPitchAlignment);
        var bufferDescription = new ResourceDescription(
            ResourceDimension.Buffer,
            0,
            (ulong)(rowPitch * height),
            1,
            1,
            1,
            Format.Unknown,
            1,
            0,
            TextureLayout.RowMajor,
            ResourceFlags.None);
        var readbackBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Readback, 0, 0),
            HeapFlags.None,
            bufferDescription,
            ResourceStates.CopyDest,
            null);

        try
        {
            var footprint = new PlacedSubresourceFootPrint
            {
                Offset = 0,
                Footprint = new SubresourceFootPrint(
                    format,
                    (uint)width,
                    (uint)height,
                    1,
                    (uint)rowPitch)
            };

            Dx12TextureUploader.Transition(
                commandList,
                backBuffer,
                ResourceStates.Present,
                ResourceStates.CopySource);
            commandList.CopyTextureRegion(
                new TextureCopyLocation(readbackBuffer, footprint),
                0,
                0,
                0,
                new TextureCopyLocation(backBuffer, 0),
                null);
            Dx12TextureUploader.Transition(
                commandList,
                backBuffer,
                ResourceStates.CopySource,
                ResourceStates.Present);

            return new Dx12BackBufferCapture(
                readbackBuffer,
                width,
                height,
                rowPitch,
                format,
                colorSpace);
        }
        catch
        {
            readbackBuffer.Dispose();
            throw;
        }
    }

    public unsafe Dx12ScreenshotImage ReadPixels()
    {
        var pixels = GC.AllocateUninitializedArray<byte>(checked(Width * Height * BytesPerPixel));
        void* mapped;
        _readbackBuffer.Map(0, null, &mapped).CheckError();
        try
        {
            var rowBytes = Width * BytesPerPixel;
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add((nint)mapped, y * _rowPitch),
                    pixels,
                    y * rowBytes,
                    rowBytes);
            }
        }
        finally
        {
            _readbackBuffer.Unmap(0, null);
        }

        return new Dx12ScreenshotImage(Width, Height, Format, ColorSpace, pixels);
    }

    public void Dispose() => _readbackBuffer.Dispose();

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);
}

internal sealed record Dx12ScreenshotImage(
    int Width,
    int Height,
    Format Format,
    ColorSpaceType ColorSpace,
    byte[] Pixels);
