using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed record EffectVariantChoice(SacredParticleDefinition Definition)
{
    public override string ToString() => Definition.IsEventPreset
        ? $"{Definition.DisplayName ?? Definition.NativeClass} (event {Definition.Preset})"
        : $"Script creation ({Definition.Status})";
}
