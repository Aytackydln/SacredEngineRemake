using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.World.Rendering;

namespace ParticleEmitterDataset;

internal sealed class FixtureEvidence(string game, string output)
{
    public async Task Export()
    {
        var selected = JsonSerializer.Deserialize<uint[]>(File.ReadAllText(Path.Combine(output, "selected-static-ids.json")))!.ToHashSet();
        var items = ItemsPakArchive.Load(Path.Combine(game, "pak/Items.pak")).ToDictionary(i => i.ItemIndex);
        var mixed = MixedPakArchive.Load(Path.Combine(game, "pak/mixed.pak"));
        using var textures = TexturePakArchive.LoadFromDirectory(Path.Combine(game, "pak"));
        var provider = new WorldStaticSpriteProvider(textures, mixed, items);
        Directory.CreateDirectory(Path.Combine(output, "fixtures"));
        Directory.CreateDirectory(Path.Combine(output, "crops"));
        var seen = new HashSet<uint>(); var rows = new List<object>();
        foreach (var line in File.ReadLines(Path.Combine(output, "Static.pak.jsonl")))
        {
            using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
            if (!selected.Contains(row.GetProperty("record_id").GetUInt32())) continue;
            var instance = row.GetProperty("known").Deserialize<StaticWorldObject>(JsonOutput.Options);
            if (!seen.Add(instance.TypeId)) continue;
            var sprite = await provider.LoadAsync(instance);
            if (sprite is null) continue;
            var relative = $"fixtures/item-{instance.TypeId}.png";
            TextureEvidence.SavePixels(Path.Combine(output, relative), sprite.Width, sprite.Height, sprite.Rgba);
            rows.Add(new { item_id = instance.TypeId, model_name = items[(ushort)instance.TypeId].ModelName,
                decoded_sprite = relative, sprite.Width, sprite.Height, sprite.AnchorX, sprite.AnchorY,
                evidence_kind = "Decoded authored fixture sprite using repository loader; not a screenshot and not the runtime particle layer." });
        }
        JsonOutput.Lines(Path.Combine(output, "fixture-sprites.jsonl"), rows);
        if (File.Exists(Path.Combine(output, "observations.jsonl")))
        {
            foreach (var line in File.ReadLines(Path.Combine(output, "observations.jsonl")))
            {
                using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
                var source = row.GetProperty("source_image").GetString()!;
                var rectangle = row.GetProperty("crop_xywh").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                using var bitmap = new Bitmap(Path.Combine(output, source));
                using var crop = bitmap.Clone(new Rectangle(rectangle[0], rectangle[1], rectangle[2], rectangle[3]), PixelFormat.Format32bppArgb);
                crop.Save(Path.Combine(output, row.GetProperty("crop_image").GetString()!), ImageFormat.Png);
            }
        }
        Console.WriteLine($"Decoded {rows.Count} fixture sprites and copied observation pixel regions.");
    }
}
