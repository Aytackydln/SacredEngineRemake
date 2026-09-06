using System;
using System.IO;
using Starward.Codec.UltraHdr;

namespace Sacred.Engine.Graphics;

/// <summary>Writes an HDR10 back buffer as a backward-compatible Ultra HDR JPEG.</summary>
internal static class Dx12HdrScreenshotWriter
{
    public static void Save(Dx12ScreenshotImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var encoder = new UhdrEncoder();
        encoder.SetRawImage(
            UhdrImageLabel.HDR,
            checked((uint)image.Width),
            checked((uint)image.Height),
            image.Pixels,
            UhdrPixelFormat._32bppRGBA1010102,
            UhdrColorGamut.BT2100,
            UhdrColorTransfer.PQ,
            UhdrColorRange.FullRange);
        encoder.SetQuality(95, UhdrImageLabel.Base);
        encoder.SetQuality(90, UhdrImageLabel.GainMap);
        encoder.SetGainmapScaleFactor(4);
        encoder.SetMutliChannelGainmap(true);
        encoder.SetPreset(UhdrEncodePreset.BestQuality);
        encoder.SetOutputFormat(UhdrImageFormat.JPEG);
        encoder.Encode();

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(encoder.GetEncodedBytes());
    }
}
