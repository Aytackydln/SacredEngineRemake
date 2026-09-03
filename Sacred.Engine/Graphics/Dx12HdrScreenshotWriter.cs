using System;
using System.Buffers.Binary;
using System.IO;
using Vortice.WIC;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace Sacred.Engine.Graphics;

/// <summary>Converts an HDR10 back buffer to a Windows-native linear scRGB JPEG XR.</summary>
internal static class Dx12HdrScreenshotWriter
{
    private const float ScrgbOneNits = 80.0f;

    public static unsafe void Save(Dx12ScreenshotImage image, string path)
    {
        var pixels = ConvertToScrgbHalf(image);
        using var factory = new IWICImagingFactory2();
        using var stream = factory.CreateStream(path, FileAccess.Write);
        using var encoder = factory.CreateEncoder(ContainerFormatGuids.Wmp, stream);
        using var frame = encoder.CreateNewFrame(out var properties);
        using (properties)
        {
            frame.Initialize(properties);
        }

        frame.SetSize((uint)image.Width, (uint)image.Height);
        frame.SetResolution(96.0, 96.0);
        frame.SetPixelFormat(WicPixelFormat.Format64bppRGBAHalf);

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

    internal static byte[] ConvertToScrgbHalf(Dx12ScreenshotImage image)
    {
        var destination = GC.AllocateUninitializedArray<byte>(
            checked(image.Width * image.Height * 8));
        for (var pixelIndex = 0; pixelIndex < image.Width * image.Height; pixelIndex++)
        {
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(
                image.Pixels.AsSpan(pixelIndex * 4, 4));
            var red2020 = PqToNits((packed & 0x3ff) / 1023.0f);
            var green2020 = PqToNits(((packed >> 10) & 0x3ff) / 1023.0f);
            var blue2020 = PqToNits(((packed >> 20) & 0x3ff) / 1023.0f);

            // Linear BT.2020 to linear BT.709/sRGB primaries. scRGB allows negative
            // components, so retain out-of-gamut values instead of clipping them.
            var red709 =
                1.660491f * red2020 - 0.587641f * green2020 - 0.072850f * blue2020;
            var green709 =
                -0.124550f * red2020 + 1.132900f * green2020 - 0.008349f * blue2020;
            var blue709 =
                -0.018151f * red2020 - 0.100579f * green2020 + 1.118730f * blue2020;

            var target = destination.AsSpan(pixelIndex * 8, 8);
            WriteHalf(target, red709 / ScrgbOneNits);
            WriteHalf(target[2..], green709 / ScrgbOneNits);
            WriteHalf(target[4..], blue709 / ScrgbOneNits);
            WriteHalf(target[6..], 1.0f);
        }

        return destination;
    }

    internal static float PqToNits(float pq)
    {
        const float m1 = 2610.0f / 16384.0f;
        const float m2 = 2523.0f / 32.0f;
        const float c1 = 3424.0f / 4096.0f;
        const float c2 = 2413.0f / 128.0f;
        const float c3 = 2392.0f / 128.0f;

        var power = MathF.Pow(MathF.Max(pq, 0.0f), 1.0f / m2);
        var numerator = MathF.Max(power - c1, 0.0f);
        var denominator = c2 - c3 * power;
        return 10_000.0f * MathF.Pow(numerator / denominator, 1.0f / m1);
    }

    private static void WriteHalf(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt16LittleEndian(
            destination,
            BitConverter.HalfToInt16Bits((Half)value));
}
