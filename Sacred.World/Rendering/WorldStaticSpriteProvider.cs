using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.World.Particles;

namespace Sacred.World.Rendering;

/// <summary>Resolves the 2D world-object formats used by Static.pak records.</summary>
public sealed class WorldStaticSpriteProvider(
    TexturePakArchive textures,
    MixedPakArchive mixed,
    IReadOnlyDictionary<ushort, ItemsPakEntry> items)
{
    private const int MiniObjectAnchorX = 48;

    private readonly Dictionary<SpriteKey, Task<WorldStaticSprite?>> _loads = [];
    private readonly Dictionary<string, Task<TextureAsset>> _textureLoads = new(StringComparer.OrdinalIgnoreCase);

    public ItemsPakEntry? GetItem(uint typeId) =>
        typeId <= ushort.MaxValue && items.TryGetValue((ushort)typeId, out var item) ? item : null;

    public Task<WorldStaticSprite?> LoadAsync(StaticWorldObject staticObject)
    {
        var item = GetItem(staticObject.TypeId);
        if (item is null)
            return Task.FromResult<WorldStaticSprite?>(null);

        SpriteKey key;
        Func<Task<WorldStaticSprite?>> factory;
        if (item.Value.MixedBaseGroupId != 0 && mixed.ResolveGroupId(item.Value.MixedBaseGroupId) is { } groupId)
        {
            key = new SpriteKey(groupId, 0, 0, 0, 0);
            factory = () => LoadMixedAsync(groupId);
        }
        else if (TryGetMiniObject(
                     item.Value,
                     staticObject.SpriteParam2E,
                     staticObject.SpriteParam2F,
                     staticObject.OrientationOrFrame,
                     staticObject.AnimationFrameDurationTicks,
                     staticObject.AnimationFrameCount,
                     out var mini))
        {
            key = new SpriteKey(0, mini.TextureId, mini.SourceX, mini.SourceY, mini.SourceSize);
            factory = () => LoadMiniObjectAsync(mini);
        }
        else
        {
            return Task.FromResult<WorldStaticSprite?>(null);
        }

        lock (_loads)
        {
            if (_loads.TryGetValue(key, out var load))
                return load;
            load = factory();
            _loads.Add(key, load);
            return load;
        }
    }

    private async Task<WorldStaticSprite?> LoadMixedAsync(uint groupId)
    {
        var group = mixed.GetGroupInfo(groupId);
        if (group is null || group.Pieces.Count == 0)
            return null;
        var pieces = group.Pieces;

        var blits = new List<SpriteBlit>();
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var piece in pieces)
        {
            if (string.IsNullOrWhiteSpace(piece.AtlasName))
                continue;
            TextureAsset atlas;
            try
            {
                atlas = await LoadTextureAsync(piece.AtlasName).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                continue;
            }

            var sourceLeft = Math.Clamp((int)MathF.Round(MathF.Min(piece.Uv0, piece.Uv2) * atlas.Width), 0, atlas.Width);
            var sourceTop = Math.Clamp((int)MathF.Round(MathF.Min(piece.Uv1, piece.Uv3) * atlas.Height), 0, atlas.Height);
            var sourceRight = Math.Clamp((int)MathF.Round(MathF.Max(piece.Uv0, piece.Uv2) * atlas.Width), 0, atlas.Width);
            var sourceBottom = Math.Clamp((int)MathF.Round(MathF.Max(piece.Uv1, piece.Uv3) * atlas.Height), 0, atlas.Height);
            var destLeft = Math.Min(piece.Left, piece.Right);
            var destTop = Math.Min(piece.Top, piece.Bottom);
            var destRight = Math.Max(piece.Left, piece.Right);
            var destBottom = Math.Max(piece.Top, piece.Bottom);
            if (sourceRight <= sourceLeft || sourceBottom <= sourceTop || destRight <= destLeft || destBottom <= destTop)
                continue;

            blits.Add(new SpriteBlit(
                atlas, sourceLeft, sourceTop, sourceRight, sourceBottom,
                destLeft, destTop, destRight, destBottom));
            minX = Math.Min(minX, destLeft);
            minY = Math.Min(minY, destTop);
            maxX = Math.Max(maxX, destRight);
            maxY = Math.Max(maxY, destBottom);
        }

        if (blits.Count == 0)
            return null;
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var pixels = new byte[checked(width * height * 4)];
        foreach (var blit in blits)
            Blit(blit, pixels, width, height, blit.DestLeft - minX, blit.DestTop - minY);
        return new WorldStaticSprite(
            groupId,
            width,
            height,
            -minX,
            -minY,
            pixels,
            group.PlacementX,
            group.PlacementY);
    }

    private async Task<WorldStaticSprite?> LoadMiniObjectAsync(MiniObjectSource source)
    {
        TextureAsset atlas;
        try
        {
            atlas = await textures.LoadTextureAsync(source.TextureId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }

        if (source.SourceSize == 0)
        {
            var frameWidth = atlas.Width / source.AtlasColumns;
            var frameHeight = atlas.Height / source.AtlasRows;
            var frame = CopyRegion(atlas, 0, 0, frameWidth, frameHeight);
            return new WorldStaticSprite(0, frameWidth, frameHeight, 0, 0, frame);
        }
        if (source.SourceX + source.SourceSize > atlas.Width || source.SourceY + source.SourceSize > atlas.Height)
            return null;
        return new WorldStaticSprite(
            0,
            source.SourceSize,
            source.SourceSize,
            MiniObjectAnchorX,
            0,
            CopyRegion(atlas, source.SourceX, source.SourceY, source.SourceSize, source.SourceSize));
    }

    private static void Blit(SpriteBlit blit, byte[] destination, int width, int height, int destinationX, int destinationY)
    {
        var drawWidth = blit.DestRight - blit.DestLeft;
        var drawHeight = blit.DestBottom - blit.DestTop;
        for (var y = 0; y < drawHeight; y++)
        for (var x = 0; x < drawWidth; x++)
        {
            var sourceX = blit.SourceLeft + x * (blit.SourceRight - blit.SourceLeft) / drawWidth;
            var sourceY = blit.SourceTop + y * (blit.SourceBottom - blit.SourceTop) / drawHeight;
            var sourceOffset = (sourceY * blit.Atlas.Width + sourceX) * 4;
            Blend(destination, width, height, destinationX + x, destinationY + y, blit.Atlas.Rgba8, sourceOffset);
        }
    }

    private static byte[] CopyRegion(TextureAsset source, int left, int top, int width, int height)
    {
        var result = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            source.Rgba8.AsSpan(((top + y) * source.Width + left) * 4, width * 4)
                .CopyTo(result.AsSpan(y * width * 4, width * 4));
        return result;
    }

    private Task<TextureAsset> LoadTextureAsync(string name)
    {
        lock (_textureLoads)
        {
            if (_textureLoads.TryGetValue(name, out var load))
                return load;
            load = textures.LoadTextureAsync(name);
            _textureLoads.Add(name, load);
            return load;
        }
    }

    private static void Blend(byte[] destination, int width, int height, int x, int y, byte[] source, int sourceOffset)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return;
        var alpha = source[sourceOffset + 3];
        if (alpha == 0)
            return;
        var offset = (y * width + x) * 4;
        var destinationAlpha = destination[offset + 3];
        var inverse = 255 - alpha;
        var outputAlpha = alpha + destinationAlpha * inverse / 255;
        if (outputAlpha == 0)
            return;
        var destinationFactor = destinationAlpha * inverse / 255;
        for (var channel = 0; channel < 3; channel++)
            destination[offset + channel] = (byte)((source[sourceOffset + channel] * alpha + destination[offset + channel] * destinationFactor) / outputAlpha);
        destination[offset + 3] = (byte)outputAlpha;
    }

    private static bool TryGetMiniObject(
        ItemsPakEntry item,
        byte sourceX,
        byte sourceY,
        byte sourceSize,
        byte frameDuration,
        byte frameCount,
        out MiniObjectSource source)
    {
        source = default;
        if (!WorldParticleMapper.TryResolveMiniObject(
                item,
                sourceX,
                sourceY,
                sourceSize,
                frameDuration,
                frameCount,
                out var reference))
        {
            return false;
        }

        source = new MiniObjectSource(
            reference.TextureId,
            reference.SourceX,
            reference.SourceY,
            reference.SourceSize,
            reference.AtlasColumns,
            reference.AtlasRows);
        return true;
    }

    private readonly record struct SpriteKey(uint GroupId, uint TextureId, int SourceX, int SourceY, int SourceSize);
    private readonly record struct MiniObjectSource(uint TextureId, int SourceX, int SourceY, int SourceSize, int AtlasColumns, int AtlasRows);
    private readonly record struct SpriteBlit(
        TextureAsset Atlas,
        int SourceLeft,
        int SourceTop,
        int SourceRight,
        int SourceBottom,
        int DestLeft,
        int DestTop,
        int DestRight,
        int DestBottom);
}

public sealed record WorldStaticSprite(
    uint GroupId,
    int Width,
    int Height,
    int AnchorX,
    int AnchorY,
    byte[] Rgba,
    int PlacementX = 0,
    int PlacementY = 0);
