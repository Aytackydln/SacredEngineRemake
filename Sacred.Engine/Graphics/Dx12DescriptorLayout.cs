using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Terrain;

namespace Sacred.Engine.Graphics;

/// <summary>Single source of truth for the shader-visible SRV heap layout.</summary>
internal static class Dx12DescriptorLayout
{
    public const int MaximumSectorTextures = 32;
    public const int MaximumModelTextures = 128;
    public const int SectorDescriptorCount = MaximumSectorTextures * Dx12SectorTextureCache.TexturesPerSector;
    public const int DebugOverlay = SectorDescriptorCount;
    public const int DebugSceneDim = DebugOverlay + 1;
    public const int ImGuiFont = DebugSceneDim + 1;
    public const int Screen = ImGuiFont + 1;
    public const int SceneColor = Screen + 1;
    public const int Fsr2History = SceneColor + 1;
    public const int FirstModelTexture = Fsr2History + 1;
    public const int FirstStaticSprite = FirstModelTexture + MaximumModelTextures;
    public const int LightHalo = FirstStaticSprite + Dx12SpritePass.MaximumTextureCount;
    public const int SurfaceLightMap = LightHalo + 1;
    public const int ShadowMap = SurfaceLightMap + 1;
    public const int PlayerOcclusionMap = ShadowMap + 1;
    public const int FirstMinimap = PlayerOcclusionMap + 1;
    public const int FirstHudTexture = FirstMinimap + Dx12MinimapPass.DescriptorsPerFrame * Dx12DeviceContext.FrameCount;
    public const int MaximumHudTextures = 7;
    public const int EscapeMenuTexture = FirstHudTexture + MaximumHudTextures;
    public const int FirstWorldMapUiTexture = EscapeMenuTexture + 1;
    public const int MaximumWorldMapUiTextures = 2;
    public const int FirstEquipmentArt = FirstWorldMapUiTexture + MaximumWorldMapUiTextures;
    public const int MaximumEquipmentAtlases = 64;
    public const int FirstEquipmentPreview = FirstEquipmentArt + MaximumEquipmentAtlases;
    public const int EquipmentDescriptorsPerFrame = 128;
    public const int FirstPortraitTexture = FirstEquipmentPreview + EquipmentDescriptorsPerFrame * Dx12DeviceContext.FrameCount;
    public const int MaximumPortraitTextures = 64;
    public const int FirstNpcOverheadTexture = FirstPortraitTexture + MaximumPortraitTextures;
    public const int MaximumNpcOverheadTextures = 64;
    public const int FirstWeatherTexture = FirstNpcOverheadTexture + MaximumNpcOverheadTextures;
    public const int TotalCount = FirstWeatherTexture + 2;
}
