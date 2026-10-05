using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World;

namespace Sacred.Engine.Assets;

/// <summary>Loads only native footprint names, retaining one atlas per surface kind.</summary>
internal sealed class FootprintSpriteLoader(AssetManager assets)
{
    private readonly Dictionary<TerrainFootprintKind, Task<StaticSpriteAsset?>> _loads = [];

    public StaticSpriteAsset? GetOrRequest(TerrainFootprintKind kind)
    {
        if (!_loads.TryGetValue(kind, out var load))
            _loads[kind] = load = LoadAsync(kind);
        return load.IsCompletedSuccessfully ? load.Result : null;
    }

    private async Task<StaticSpriteAsset?> LoadAsync(TerrainFootprintKind kind)
    {
        try
        {
            var format = TerrainFootprintTextures.FrameNameFormat(kind);
            if (format is null) return null;
            var count = TerrainFootprintTextures.FrameCount(kind);
            var frames = new TextureAsset[count];
            for (var i = 0; i < count; i++)
                frames[i] = await assets.LoadTextureAsync(string.Format(CultureInfo.InvariantCulture,
                    format, i + 1)).ConfigureAwait(false);
            var width = frames[0].Width;
            var height = frames[0].Height;
            var columns = TextureFrameAtlasLayout.CalculateColumns(width, height, count);
            var rows = TextureFrameAtlasLayout.CalculateRows(count, columns);
            var pixels = new byte[checked(width * columns * height * rows * 4)];
            for (var i = 0; i < count; i++)
            {
                if (frames[i].Width != width || frames[i].Height != height)
                    throw new InvalidOperationException("Footprint frame dimensions differ.");
                for (var y = 0; y < height; y++)
                    frames[i].Rgba8.AsSpan(y * width * 4, width * 4).CopyTo(pixels.AsSpan(
                        ((i / columns * height + y) * width * columns + i % columns * width) * 4, width * 4));
            }
            EngineLog.WriteLine($"Footprint textures loaded: {kind}, {count} frame(s), {width}x{height}.");
            return new StaticSpriteAsset(0, width, height, 0, 0, pixels, count,
                count > 1 ? FootprintTrail.WaterFrameSeconds : 0);
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Footprint textures failed: {kind}: {exception.Message}");
            return null;
        }
    }
}
