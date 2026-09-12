using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ParticleEmitterDataset;

internal sealed record Scene(string Id, int WorldX, int WorldY, int Width, int Height,
    double PlayerX, double PlayerY, IReadOnlyList<Mark> Marks);
internal sealed record Mark(string Id, int X, int Y, int Width, int Height, int PixelCount);

internal static class ImageEvidence
{
    public static List<Scene> Export(string input, string output)
    {
        Directory.CreateDirectory(Path.Combine(output, "images"));
        var scenes = new List<Scene>();
        var provenance = new List<object>();
        foreach (var file in Directory.GetFiles(input, "*.png").Where(p => !p.Contains("underlined")))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var xy = stem.Split(' ').Select(int.Parse).ToArray();
            var markedPath = Path.Combine(input, stem + " underlined.png");
            using var clean = new Bitmap(file);
            using var marked = new Bitmap(markedPath);
            var overlap = new Rectangle(0, 0, Math.Min(clean.Width, marked.Width), Math.Min(clean.Height, marked.Height));
            using var overlapClean = clean.Clone(overlap, PixelFormat.Format32bppArgb);
            using var overlapMarked = marked.Clone(overlap, PixelFormat.Format32bppArgb);
            var a = Pixels(overlapClean); var b = Pixels(overlapMarked);
            var mask = new bool[overlap.Width * overlap.Height];
            var changed = 0; var greenChanged = 0;
            for (var i = 0; i < mask.Length; i++)
            {
                var p = i * 4;
                var different = a[p] != b[p] || a[p+1] != b[p+1] || a[p+2] != b[p+2] || a[p+3] != b[p+3];
                if (different) changed++;
                mask[i] = different && b[p+1] > 180 && b[p+2] < 140 && b[p] < 140 && b[p+1] > b[p+2] + 70;
                if (mask[i]) greenChanged++;
            }
            var components = Components(mask, overlap.Width, overlap.Height);
            var id = stem.Replace(' ', '_');
            var marks = components.OrderBy(c => c.Y).ThenBy(c => c.X)
                .Select((c, i) => new Mark($"{id}_P{i+1:00}", c.X, c.Y, c.W, c.H, c.N)).ToArray();
            var anchor = xy[0] switch
            {
                2146 => (858d, 550d), 2262 => (842d, 552d), 2286 => (850d, 551d),
                2551 => (854d, 550d), 3191 => (853d, 551d), 3501 => (841d, 552d),
                4585 => (841d, 546d), _ => (854d, 551d)
            };
            scenes.Add(new Scene(id, xy[0], xy[1], clean.Width, clean.Height, anchor.Item1, anchor.Item2, marks));
            foreach (var path in new[] { file, markedPath })
            {
                var relative = "images/" + Path.GetFileName(path);
                File.Copy(path, Path.Combine(output, relative), true);
                provenance.Add(new { scene_id = id, original_path = path, relative_path = relative,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                    byte_length = new FileInfo(path).Length, width = path == file ? clean.Width : marked.Width,
                    height = path == file ? clean.Height : marked.Height });
            }
            JsonOutput.Write(Path.Combine(output, id + ".pair.json"), new
            {
                scene_id = id, changed_pixels = changed, green_changed_pixels = greenChanged,
                compared_rectangle = new { x = 0, y = 0, width = overlap.Width, height = overlap.Height },
                clean_width = clean.Width, clean_height = clean.Height, marked_width = marked.Width, marked_height = marked.Height,
                alignment = "Top-left aligned common rectangle; right/bottom excess pixels are not compared.",
                non_green_changed_pixels = changed - greenChanged, marks,
                detection = "Changed pixels with G>180, R<140, B<140, G>R+70; 8-connected components with >=8 pixels and width>=8. Coordinates are original image pixels, origin top-left."
            });
            Console.WriteLine($"Image pair {id}: {clean.Width}x{clean.Height}, {marks.Length} green underlines, {changed} changed pixels.");
        }
        JsonOutput.Write(Path.Combine(output, "image-manifest.json"), provenance);
        JsonOutput.Write(Path.Combine(output, "scene-inputs.json"), scenes);
        return scenes;
    }

    private static byte[] Pixels(Bitmap source)
    {
        using var image = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[image.Width * image.Height * 4];
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * image.Width * 4, image.Width * 4);
            return bytes;
        }
        finally { image.UnlockBits(data); }
    }

    private static List<(int X, int Y, int W, int H, int N)> Components(bool[] mask, int width, int height)
    {
        var result = new List<(int, int, int, int, int)>(); var queue = new Queue<int>();
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            mask[i] = false; queue.Enqueue(i);
            var x0 = i % width; var y0 = i / width; var x1 = x0; var y1 = y0; var count = 0;
            while (queue.TryDequeue(out var p))
            {
                var x = p % width; var y = p / width;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); count++;
                for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx; var ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    var n = ny * width + nx;
                    if (!mask[n]) continue;
                    mask[n] = false; queue.Enqueue(n);
                }
            }
            if (count >= 8 && x1 - x0 >= 7) result.Add((x0, y0, x1-x0+1, y1-y0+1, count));
        }
        return result;
    }
}
