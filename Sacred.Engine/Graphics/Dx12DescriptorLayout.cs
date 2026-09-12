using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Sprites;

namespace Sacred.Engine.Graphics;

/// <summary>Single source of truth for the shader-visible SRV heap layout.</summary>
internal static class Dx12DescriptorLayout
{
    public const int MaximumSectorTextures = 32;
    public const int MaximumModelTextures = 128;
    public const int SectorDescriptorCount = MaximumSectorTextures * 5;
    public const int DebugOverlay = SectorDescriptorCount;
    public const int DebugSceneDim = DebugOverlay + 1;
    public const int ImGuiFont = DebugSceneDim + 1;
    public const int Screen = ImGuiFont + 1;
    public const int SceneColor = Screen + 1;
    public const int FirstModelTexture = SceneColor + 1;
    public const int FirstStaticSprite = FirstModelTexture + MaximumModelTextures;
    public const int LightHalo = FirstStaticSprite + Dx12SpritePass.MaximumTextureCount;
    public const int SurfaceLightMap = LightHalo + 1;
    public const int PlayerOcclusionMap = SurfaceLightMap + 1;
    public const int FirstMinimap = PlayerOcclusionMap + 1;
    public const int TotalCount = FirstMinimap + Dx12MinimapPass.DescriptorsPerFrame * Dx12DeviceContext.FrameCount;
}
