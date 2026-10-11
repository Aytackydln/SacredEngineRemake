using System;
using System.Threading.Tasks;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Direct authored surfaces that require textures but no model archive lookup.</summary>
internal static class FxDirectPreviewLoader
{
    public static bool Supports(SacredParticleDefinition definition) => definition.BubbleTrapExplosion is not null || definition.TerrorSprinter is not null || definition.WarCrySign is not null || definition.PulseFlare is not null || definition.ColumnFlare is not null || definition.SpriteFlare is not null || definition.ControlledFlare is not null || definition.PongBatter is not null || definition.LayeredFlare is not null || definition.StrikeSymbol is not null || definition.RingShield is not null || definition.Confusion is not null;
    public static Task LoadAsync(ModelPreviewPane preview, SacredParticleCatalogue catalogue,
        string effect, SacredParticleDefinition definition)
    {
        Console.WriteLine($"[Assets] FX selected: {definition.TypeId} {effect}; family={definition.NativeClass}; preset={definition.Preset}.");
        return preview.LoadAsync(effect, _ => Task.FromResult(new GrnAsset(effect, [], null, new Mesh([], []))), [],
            fxFactory: _ => definition.BubbleTrapExplosion is { } bubble ? new FxPreviewPlayback(catalogue, bubble)
                : definition.TerrorSprinter is { } terror ? new FxPreviewPlayback(catalogue, terror)
                : definition.WarCrySign is { } sign ? new FxPreviewPlayback(catalogue, sign)
                : definition.PulseFlare is { } pulse ? new FxPreviewPlayback(catalogue, pulse)
                : definition.ColumnFlare is { } column ? new FxPreviewPlayback(catalogue, column)
                : definition.SpriteFlare is { } sprite ? new FxPreviewPlayback(catalogue, sprite)
                : definition.ControlledFlare is { } controlled ? new FxPreviewPlayback(catalogue, controlled)
                : definition.LayeredFlare is { } flare ? new FxPreviewPlayback(catalogue, flare)
                : definition.StrikeSymbol is { } strike ? new FxPreviewPlayback(catalogue, strike)
                : definition.RingShield is { } ring ? new FxPreviewPlayback(catalogue, ring)
                : definition.Confusion is { } confusion ? new FxPreviewPlayback(catalogue, confusion)
                : new FxPreviewPlayback(catalogue, definition.PongBatter!));
    }
}
