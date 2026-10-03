using System;
using System.IO;
using Sacred.Engine.Graphics.Swapchain;

namespace Sacred.Engine.Graphics;

/// <summary>Routes swap-chain pixels to the native SDR or HDR screenshot format.</summary>
internal static class Dx12ScreenshotWriter
{
    public static string CreatePath(
        string gameDirectory,
        string? label,
        Dx12ScreenshotImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(image);

        var directory = Path.Combine(gameDirectory, "Screenshots", "Remake");
        Directory.CreateDirectory(directory);
        var extension = IsHdr(image) ? ".jxr" : ".png";
        return Path.Combine(
            directory,
            $"{DateTime.Now:yyyyMMdd-HHmmssfff}{SanitizeLabel(label)}{extension}");
    }

    public static void Save(Dx12ScreenshotImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (IsHdr(image))
        {
            Dx12HdrScreenshotWriter.Save(image, path);
            return;
        }

        if (image.Format != Dx12SdrSwapChain.SdrBackBufferFormat ||
            image.ColorSpace != Dx12SdrSwapChain.SdrColorSpace)
            throw UnsupportedFormat(image);

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        PngScreenshotEncoder.WriteSignature(output);
        PngScreenshotEncoder.WriteHeader(output, image.Width, image.Height, 8, 2);
        WriteSrgbProfile(output);
        PngScreenshotEncoder.WriteImageData(
            output,
            image.Height,
            checked(image.Width * 3),
            (y, destination) => CopySdrRow(image, y, destination));
        PngScreenshotEncoder.WriteChunk(output, "IEND"u8, []);
    }

    public static string DescribeColorSpace(Dx12ScreenshotImage image) => image.ColorSpace switch
    {
        Dx12SdrSwapChain.SdrColorSpace => "SDR sRGB / Rec.709",
        Dx12HdrSwapChain.HdrColorSpace => "HDR JPEG XR / Rec.2020 PQ",
        _ => image.ColorSpace.ToString()
    };

    private static void WriteSrgbProfile(Stream output)
    {
        // Force the UNORM back-buffer bytes to sRGB. Without this chunk, readers can
        // interpret the values as linear and produce the classic washed-out capture.
        PngScreenshotEncoder.WriteChunk(output, "sRGB"u8, [0]); // Perceptual rendering intent.
    }

    private static void CopySdrRow(Dx12ScreenshotImage image, int y, Span<byte> destination)
    {
        var source = image.Pixels.AsSpan(y * image.Width * 4, image.Width * 4);
        for (var x = 0; x < image.Width; x++)
        {
            destination[x * 3] = source[x * 4 + 2];
            destination[x * 3 + 1] = source[x * 4 + 1];
            destination[x * 3 + 2] = source[x * 4];
        }
    }

    private static bool IsHdr(Dx12ScreenshotImage image) =>
        image.Format == Dx12HdrSwapChain.HdrBackBufferFormat &&
        image.ColorSpace == Dx12HdrSwapChain.HdrColorSpace;

    private static NotSupportedException UnsupportedFormat(Dx12ScreenshotImage image) => new(
        $"Screenshot format {image.Format} in {image.ColorSpace} is not supported.");

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var characters = label.Trim().ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (Array.IndexOf(invalid, characters[index]) >= 0)
                characters[index] = '_';
        }

        return "-" + new string(characters);
    }
}
