using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Decoded render paths supported by the viewer. Some catalogue entries
/// describe native controller bindings and intentionally have no preview draw.</summary>
internal static class FxPreviewSupport
{
    internal static bool CanPreview(SacredParticleDefinition definition) =>
        definition.ElementStone is not null || definition.ForgeElement is not null ||
        definition.UnderworldGlyph is not null || definition.Stoned is not null ||
        definition.DoorSpike is not null || definition.Anducar is not null ||
        FxDirectPreviewLoader.Supports(definition) ||
        definition.Status == SacredParticleDefinitionStatus.Decoded && definition.Draw is not null;
}
