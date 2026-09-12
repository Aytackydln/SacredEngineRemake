using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Assets.Paks.Texture;

namespace ParticleEmitterDataset;

internal sealed class TextureEvidence(string pak, string output)
{
    public async Task Export(TexturePakArchive archive)
    {
        Directory.CreateDirectory(Path.Combine(output, "textures"));
        var rows = new List<object>();
        foreach (var path in Directory.GetFiles(pak, "texture*.pak").Order())
        {
            using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
            stream.Position = 4; var count = reader.ReadInt32();
            for (var id = 0; id < count; id++)
            {
                stream.Position = 0x100 + id * 12 + 4;
                var offset = reader.ReadUInt32(); var size = reader.ReadUInt32();
                if (offset == 0 || size == 0 || offset + 32 > stream.Length) continue;
                stream.Position = offset;
                var nameBytes = reader.ReadBytes(32);
                var end = Array.IndexOf(nameBytes, (byte)0);
                var name = Encoding.Latin1.GetString(nameBytes, 0, end < 0 ? 32 : end);
                if (!name.StartsWith("PARTICLE_", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("FX_", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("MINIOBJ4X4", StringComparison.OrdinalIgnoreCase)) continue;
                if (!archive.TryResolveTextureRecord(name, out var record)) continue;
                var texture = await archive.LoadTextureAsync(name);
                var relative = "textures/" + name + ".png";
                SavePixels(Path.Combine(output, relative), texture.Width, texture.Height, texture.Rgba8);
                var a0 = 0; var a255 = 0; var apart = 0; var black = 0;
                for (var p = 0; p < texture.Rgba8.Length; p += 4)
                {
                    var a = texture.Rgba8[p+3];
                    if (a == 0) a0++; else if (a == 255) a255++; else apart++;
                    if (texture.Rgba8[p] == 0 && texture.Rgba8[p+1] == 0 && texture.Rgba8[p+2] == 0) black++;
                }
                stream.Position = offset; var header = reader.ReadBytes(64);
                rows.Add(new { name, archive = "pak/" + Path.GetFileName(path), descriptor_id = id,
                    file_offset = offset, descriptor_size = size, header_prefix_64_hex = Convert.ToHexString(header),
                    record.Width, record.Height, storage_type = record.Type, storage_format = record.StorageFormat.ToString(),
                    decoded_png = relative, transparent_pixels = a0, opaque_pixels = a255, translucent_pixels = apart,
                    black_rgb_pixels = black, association = "Candidate asset catalogue only; no link to any screenshot emitter established." });
            }
        }
        JsonOutput.Lines(Path.Combine(output, "Texture.pak.candidates.jsonl"), rows);
        Console.WriteLine($"Exported {rows.Count} particle/FX/animated mini-object texture candidates.");
    }

    public static void SavePixels(string path, int width, int height, byte[] rgba)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bgra = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i+2]; bgra[i+1] = rgba[i+1]; bgra[i+2] = rgba[i]; bgra[i+3] = rgba[i+3];
        }
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (var y = 0; y < height; y++) Marshal.Copy(bgra, y*width*4, data.Scan0+y*data.Stride, width*4); }
        finally { bitmap.UnlockBits(data); }
        bitmap.Save(path, ImageFormat.Png);
    }
}
