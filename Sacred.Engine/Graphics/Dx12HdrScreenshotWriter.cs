using System;
using System.IO;
using Vortice.WIC;

namespace Sacred.Engine.Graphics;

/// <summary>Writes an HDR10 back buffer as a JPEG XR image.</summary>
internal static unsafe class Dx12HdrScreenshotWriter
{
    public static void Save(Dx12ScreenshotImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var factory = new IWICImagingFactory2();
        using var stream = factory.CreateStream(path, FileAccess.Write);
        using var encoder = factory.CreateEncoder(ContainerFormatGuids.Wmp, stream);
        using var frame = encoder.CreateNewFrame(out var options);
        frame.Initialize(options);
        frame.SetSize((uint)image.Width, (uint)image.Height);
        frame.SetResolution(96, 96);
        var pixelFormat = PixelFormat.Format32bppRGBA1010102;
        frame.SetPixelFormat(ref pixelFormat);
        fixed (byte* pixels = image.Pixels)
        {
            frame.WritePixels(
                (uint)image.Height,
                checked((uint)(image.Width * sizeof(uint))),
                checked((uint)image.Pixels.Length),
                pixels);
        }
        frame.Commit();
        encoder.Commit();
    }
}
