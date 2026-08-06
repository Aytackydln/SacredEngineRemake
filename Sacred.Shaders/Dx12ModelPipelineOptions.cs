using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Runtime choices for the model pipeline catalog.</summary>
public sealed record Dx12ModelPipelineOptions
{
    public bool HdrOutput { get; init; }
    public bool IncludeDenseParticle { get; init; } = true;
    public bool IncludeInventoryUi { get; init; }
    public TextureAddressMode SamplerAddressMode { get; init; } = TextureAddressMode.Clamp;
    public StaticBorderColor SamplerBorderColor { get; init; } = StaticBorderColor.TransparentBlack;
}
