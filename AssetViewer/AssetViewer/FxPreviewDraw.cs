using System.Numerics;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>One range of the preview mesh and its native material composition.</summary>
internal sealed class FxPreviewDraw
{
    public int IndexStart { get; set; }
    public int IndexCount { get; set; }
    public string TextureName { get; set; } = string.Empty;
    public string? OverlayTextureName { get; set; }
    public Vector4 Color { get; set; } = Vector4.One;
    public bool Additive { get; set; }
    public bool SourceColorOnly { get; set; }
    public bool UsesCornerTint { get; set; }
    public ParticleTextureMode Mode { get; set; } = ParticleTextureMode.NativeFx;
}
