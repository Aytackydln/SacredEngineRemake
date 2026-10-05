using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Portals;

namespace Sacred.Engine.Assets;

/// <summary>Shares the native portal textures between all instances; produces no files.</summary>
internal sealed class PortalSpriteLoader(AssetManager assets)
{
    private readonly Dictionary<SacredPortalVariant, Task<StaticSpriteAsset[]?>> _loads = [];
    private readonly HashSet<SacredPortalVariant> _observedCompletion = [];
    public bool Pending => _loads.Keys.Any(variant => !_observedCompletion.Contains(variant));
    public StaticSpriteAsset[]? GetOrRequest(SacredPortalVariant variant)
    {
        if (!_loads.TryGetValue(variant, out var load)) _loads[variant] = load = LoadAsync(variant);
        if (!load.IsCompleted) return null;
        _observedCompletion.Add(variant);
        return load.IsCompletedSuccessfully ? load.Result : null;
    }

    private async Task<StaticSpriteAsset[]?> LoadAsync(SacredPortalVariant variant)
    {
        try
        {
            var frames = await Task.WhenAll(SacredPortalAppearance.GetSurfaceTextures(variant).Select(name => assets.LoadTextureAsync(name)))
                .ConfigureAwait(false);
            var width = frames[0].Width;
            var height = frames[0].Height;
            var columns = TextureFrameAtlasLayout.CalculateColumns(width, height, frames.Length);
            var rows = TextureFrameAtlasLayout.CalculateRows(frames.Length, columns);
            var pixels = new byte[checked(width * columns * height * rows * 4)];
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i].Width != width || frames[i].Height != height)
                    throw new InvalidOperationException("Portal minimap texture dimensions differ.");
                for (var y = 0; y < height; y++)
                    frames[i].Rgba8.AsSpan(y * width * 4, width * 4).CopyTo(pixels.AsSpan(
                        ((i / columns * height + y) * width * columns + i % columns * width) * 4, width * 4));
            }
            var first = await assets.LoadTextureAsync(SacredPortalAppearance.GetFirstWhirlTexture(variant)).ConfigureAwait(false);
            var second = await assets.LoadTextureAsync(SacredPortalAppearance.GetSecondWhirlTexture(variant)).ConfigureAwait(false);
            EngineLog.WriteLine($"Portal billboards loaded ({variant}): {frames.Length} minimaps, two unlit animated whirl layers.");
            return [new(0, width, height, 0, 0, pixels, frames.Length, 1),
                new(0, first.Width, first.Height, 0, 0, first.Rgba8),
                new(0, second.Width, second.Height, 0, 0, second.Rgba8)];
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Portal billboard textures failed: {exception.Message}");
            return null;
        }
    }
}
