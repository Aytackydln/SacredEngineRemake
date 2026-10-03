using System;
using System.IO;
using Vortice.WIC;

namespace Sacred.Engine.Graphics;

/// <summary>Writes a floating-point scRGB back buffer as a Windows-native HDR JPEG XR.</summary>
internal static class Dx12HdrScreenshotWriter
{
    public static unsafe void Save(Dx12ScreenshotImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);

        var pixels = image.Pixels;
        if (pixels.Length != checked(image.Width * image.Height * 8))
            throw new ArgumentException("Expected half-float scRGB RGBA pixels.", nameof(image));
        using var factory = new IWICImagingFactory2();
        using var stream = factory.CreateStream(path, FileAccess.Write);
        using var encoder = factory.CreateEncoder(ContainerFormatGuids.Wmp, stream);
        using var frame = encoder.CreateNewFrame(out var options);
        using (options)
        {
            frame.Initialize(options);
        }

        frame.SetSize((uint)image.Width, (uint)image.Height);
        frame.SetResolution(96, 96);
        var pixelFormat = PixelFormat.Format64bppRGBAHalf;
        frame.SetPixelFormat(ref pixelFormat);
        if (pixelFormat != PixelFormat.Format64bppRGBAHalf)
            throw new NotSupportedException("The JPEG XR encoder does not support half-float scRGB pixels.");

        fixed (byte* pixelPointer = pixels)
        {
            frame.WritePixels(
                (uint)image.Height,
                checked((uint)(image.Width * 8)),
                checked((uint)pixels.Length),
                pixelPointer);
        }

        frame.Commit();
        encoder.Commit();
    }
}
