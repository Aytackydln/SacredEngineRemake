using System.Drawing;
using System.Text.Json;

namespace ParticleEmitterDataset;

internal static class VerifyEvidence
{
    public static void Run(string output)
    {
        var count = 0; long pixels = 0;
        foreach (var line in File.ReadLines(Path.Combine(output, "observations.jsonl")))
        {
            using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
            var rect = row.GetProperty("crop_xywh").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            using var source = new Bitmap(Path.Combine(output, row.GetProperty("source_image").GetString()!));
            using var crop = new Bitmap(Path.Combine(output, row.GetProperty("crop_image").GetString()!));
            if (crop.Width != rect[2] || crop.Height != rect[3]) throw new InvalidDataException("Crop dimension mismatch");
            for (var y = 0; y < crop.Height; y++)
            for (var x = 0; x < crop.Width; x++)
                if (source.GetPixel(x+rect[0],y+rect[1]).ToArgb() != crop.GetPixel(x,y).ToArgb())
                    throw new InvalidDataException($"Crop pixel mismatch: {row.GetProperty("observation_id").GetString()}");
            pixels += (long)crop.Width*crop.Height; count++;
        }
        JsonOutput.Write(Path.Combine(output, "crop-verification.json"), new { verified_crop_count = count,
            compared_pixels = pixels, exact_argb_pixel_matches = true,
            method = "Every crop pixel compared to its original clean PNG pixel at the recorded crop offset, including alpha." });
        Console.WriteLine($"Verified {count} lossless crops ({pixels} pixels).");
    }
}
