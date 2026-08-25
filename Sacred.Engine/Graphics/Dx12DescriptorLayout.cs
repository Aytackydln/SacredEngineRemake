using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Sprites;

namespace Sacred.Engine.Graphics;

/// <summary>Single source of truth for the shader-visible SRV heap layout.</summary>
internal static class Dx12DescriptorLayout
{
    public const int MaximumSectorTextures = 32;
    public const int MaximumModelTextures = 128;
    public const int SectorDescriptorCount = MaximumSectorTextures * 4;
    public const int DebugOverlay = SectorDescriptorCount;
    public const int ControlsOverlay = DebugOverlay + 1;
    public const int Screen = ControlsOverlay + 1;
    public const int FirstModelTexture = Screen + 1;
    public const int FirstStaticSprite = FirstModelTexture + MaximumModelTextures;
    public const int LightHalo = FirstStaticSprite + Dx12SpritePass.MaximumTextureCount;
    public const int FirstMinimap = LightHalo + 1;
    public const int TotalCount = FirstMinimap + Dx12MinimapPass.DescriptorsPerFrame * Dx12DeviceContext.FrameCount;
}
