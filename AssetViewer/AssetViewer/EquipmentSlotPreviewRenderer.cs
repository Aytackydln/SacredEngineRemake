using System.Numerics;
using AssetViewer.ItemViewer;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Weapon;
using Sacred.Granny.Abstractions;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

/// <summary>Renders transparent item layers through the platform backend without focus.</summary>
internal sealed class EquipmentSlotPreviewRenderer(AssetViewerSession session) : IDisposable
{
    private readonly IEquipmentSlotRenderBackend _renderer = EquipmentSlotRenderFactory.Create();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<EquipmentSlotPreview> RenderAsync(SacredEquipment item, Vector2 size, bool weaponSlot, CancellationToken token)
    {
        var asset = await Task.Run(() => session.Models.LoadModelAsync(item.Item.ModelName,
            GrnMeshExtractionMode.PrimarySlice, token), token);
        var desc = item.Item.ModelDesc;
        var surfaces = asset.Mesh?.Surfaces ?? [];
        var names = surfaces.Select(s => s.TextureName).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hasEffect = session.Textures.TryGetTextureName(desc.EffectTextureId, out var effectName) &&
            names.Any(n => session.Textures.TryResolveTextureName(n!, out var resolved) &&
                resolved.Equals(effectName, StringComparison.OrdinalIgnoreCase));
        var bindings = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var reference = ModelTextureResolver.Resolve(session.Textures, desc.TextureId, desc.EffectTextureId,
                desc.GraphicFlags, hasEffect, names.Length == 1, name, desc.EffectTextureIndex);
            var texture = await session.TextureCache.LoadTextureAsync(reference.TextureName, token);
            TextureAsset? overlay = reference.HasOverlay
                ? await session.TextureCache.LoadTextureAsync(reference.OverlayTextureName!, token) : null;
            if ((reference.Animation.HasSecondaryGlow || reference.OverlayAnimation.HasSecondaryGlow) &&
                !bindings.ContainsKey(TextureAnimation.MultitextureGlowName))
                bindings[TextureAnimation.MultitextureGlowName] = new ModelTextureBinding(
                    await session.TextureCache.LoadTextureAsync(TextureAnimation.MultitextureGlowName, token));
            bindings.Add(name!, new(texture with { Animation = reference.Animation },
                overlay is null ? null : overlay with { Animation = reference.OverlayAnimation }, reference.OverlayMode));
        }
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            _renderer.SetInventoryPlacement(item.PreviewScale, item.PreviewOffset);
            _renderer.SetModel(asset, item.PreviewRotation, item.Width, item.Height, EquipmentEffectScene.Empty);
            var center = weaponSlot ? WeaponSlotLayout.ItemCenter(item) : new(item.Width * 16, item.Height * 16);
            var minimum = Vector2.Zero;
            var maximum = size;
            // Native renderMouse3D draws onto the screen, without a per-slot scissor.
            // Preserve authored model overflow while keeping the slot's hit rectangle.
            var matrix = WeaponPakPreviewTransform.CreateModelSpace(-(asset.Diagnostics?.SourceOriginOffset ?? Vector3.Zero)) *
                WeaponPakPreviewTransform.CreateWorld(item.PreviewScale,
                    WeaponPakPreviewTransform.CreateRotation(item.PreviewRotation), item.PreviewOffset);
            foreach (var vertex in asset.Mesh?.Vertices ?? [])
            {
                var position = Vector3.Transform(vertex.Position, matrix);
                var point = center + new Vector2(position.X, -position.Z);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            minimum = new(MathF.Floor(minimum.X), MathF.Floor(minimum.Y));
            maximum = new(MathF.Ceiling(maximum.X), MathF.Ceiling(maximum.Y));
            var renderSize = maximum - minimum;
            _renderer.SetEquipmentSlotViewport(renderSize, center - minimum);
            await _renderer.SetTexturesAsync(bindings, token);
            token.ThrowIfCancellationRequested();
            var bitmap = await _renderer.CaptureBitmapAsync();
            Console.WriteLine($"[Assets] Equipment slot preview ready: {item.IdemId}; {size.X}x{size.Y}; " +
                $"authored cells {item.Width}x{item.Height}; effects disabled.");
            return new(bitmap, minimum, renderSize);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _renderer?.Dispose();

        // Pending loads use their caller's cancellation token before entering the renderer.
    }
}
