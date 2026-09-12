namespace Sacred.Shaders;

public static class Dx12ShaderCatalog
{
    private const EmbeddedResource_Shaders HdrCommon = EmbeddedResource_Shaders.HdrCommon_hlsl;
    private const EmbeddedResource_Shaders SpriteCommon = EmbeddedResource_Shaders.SacredSpriteCommon_hlsl;
    private const EmbeddedResource_Shaders StaticSpriteCommon = EmbeddedResource_Shaders.SacredStaticSpriteCommon_hlsl;
    private static readonly Dx12ShaderSource ModelShadowVertexShader =
        Shader("SacredModelShadow", EmbeddedResource_Shaders.SacredModelShadow_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource ModelShadowPixelShader =
        Shader("SacredModelShadow", EmbeddedResource_Shaders.SacredModelShadow_hlsl, "ps_main", "ps_5_0");
    private static readonly Dx12ShaderSource GroundShadowVertexShader =
        Shader("SacredGroundShadow", EmbeddedResource_Shaders.SacredGroundShadow_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource GroundShadowPixelShader =
        Shader("SacredGroundShadow", EmbeddedResource_Shaders.SacredGroundShadow_hlsl, "ps_main", "ps_5_0");

    private static readonly Dx12ShaderSource QuadWorldVertexShader =
        DisplayShader("SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource StaticSpriteVertexShader =
        DisplayStaticSpriteShader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource StaticSpriteShadowVertexShader =
        Shader("SacredStaticSpriteShadow", EmbeddedResource_Shaders.SacredStaticSpriteShadow_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource StaticSpriteShadowPixelShader =
        Shader("SacredStaticSpriteShadow", EmbeddedResource_Shaders.SacredStaticSpriteShadow_hlsl, "ps_main", "ps_5_0");
    internal static readonly Dx12ShaderSource SurfaceLightMapVertexShader =
        Shader("SacredSurfaceLightMap", EmbeddedResource_Shaders.SacredSurfaceLightMap_hlsl, "vs_main", "vs_5_0");
    internal static readonly Dx12ShaderSource SurfaceLightMapPixelShader =
        Shader("SacredSurfaceLightMap", EmbeddedResource_Shaders.SacredSurfaceLightMap_hlsl, "ps_main", "ps_5_0");
    internal static readonly Dx12ShaderSource PlayerOcclusionMapVertexShader =
        Shader("SacredPlayerOcclusionMap", EmbeddedResource_Shaders.SacredPlayerOcclusionMap_hlsl, "vs_main", "vs_5_0");
    internal static readonly Dx12ShaderSource PlayerOcclusionMapPixelShader =
        Shader("SacredPlayerOcclusionMap", EmbeddedResource_Shaders.SacredPlayerOcclusionMap_hlsl, "ps_main", "ps_5_0");
    private static readonly Dx12ShaderSource ModelVertexShader =
        DisplayShader("SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource AnimatedModelVertexShader =
        DisplayShader("SacredAnimatedModel", EmbeddedResource_Shaders.SacredAnimatedModel_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource EffectModelVertexShader =
        DisplayShader("SacredEffectModel", EmbeddedResource_Shaders.SacredEffectModel_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource ItemParticleVertexShader =
        DisplayShader("SacredItemParticle", EmbeddedResource_Shaders.SacredItemParticle_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource ItemGlowVertexShader =
        DisplayShader("SacredItemGlow", EmbeddedResource_Shaders.SacredItemGlow_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource InventoryUiVertexShader =
        DisplayShader("SacredInventoryUi", EmbeddedResource_Shaders.SacredInventoryUi_hlsl, "vs_main", "vs_5_0");
    internal static readonly Dx12ShaderSource ImGuiVertexShader =
        DisplayShader("SacredImGui", EmbeddedResource_Shaders.SacredImGui_hlsl, "vs_main", "vs_5_0");

    // Sector composition is display-independent; SDR/HDR conversion happens later when the
    // completed sector texture is sampled by the world-quad shader.
    public static readonly Dx12ShaderSource TerrainComposeVertexShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "vs_main", "vs_5_0");
    public static readonly Dx12ShaderSource TerrainComposePixelShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "ps_main", "ps_5_0");
    public static readonly Dx12ShaderSource SectorSpriteComposeVertexShader =
        Shader("SacredSectorSpriteCompose", EmbeddedResource_Shaders.SacredSectorSpriteCompose_hlsl, "vs_main", "vs_5_0");
    public static readonly Dx12ShaderSource SectorSpriteComposePixelShader =
        Shader("SacredSectorSpriteCompose", EmbeddedResource_Shaders.SacredSectorSpriteCompose_hlsl, "ps_main", "ps_5_0");

    public static readonly Dx12ShaderSet Sdr = CreateShaderSet("vs_sdr", "ps_sdr");
    public static readonly Dx12ShaderSet Hdr = CreateShaderSet("vs_hdr", "ps_hdr");

    internal static Dx12ShaderSource GetImGuiPixelShader(bool hdrOutput) =>
        DisplayShader(
            "SacredImGui",
            EmbeddedResource_Shaders.SacredImGui_hlsl,
            hdrOutput ? "ps_hdr" : "ps_sdr",
            "ps_5_0");

    public static Dx12ShaderSource GetUpscalePixelShader(bool hdrOutput) =>
        DisplayShader(
            "SacredWorldQuad",
            EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
            hdrOutput ? "ps_hdr_upscale" : "ps_sdr_upscale",
            "ps_5_0");

    /// <summary>Raised after the embedded shader assembly is rebuilt.</summary>
    public static event Action? Reloaded;

    static Dx12ShaderCatalog() => EmbeddedShaderAssemblyReloader.WatchForRebuilds(() => Reloaded?.Invoke());

    private static Dx12ShaderSource Shader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string target) =>
        new(name, [() => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())], entryPoint, target);

    private static Dx12ShaderSet CreateShaderSet(
        string lightHaloVertexEntryPoint,
        string pixelEntryPoint) => new(
        QuadWorldVertexShader,
        DisplayShader("SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl, pixelEntryPoint, "ps_5_0"),
        DisplayShader(
            "SacredWorldQuad",
            EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_screen" : "ps_sdr_screen",
            "ps_5_0"),
        StaticSpriteVertexShader,
        DisplayStaticSpriteShader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, pixelEntryPoint, "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSprite",
            EmbeddedResource_Shaders.SacredStaticSprite_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_transparent_hdr" : "ps_transparent_sdr",
            "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSpriteUnlit",
            EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_unlit_hdr" : "ps_unlit_sdr",
            "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSpriteUnlit",
            EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_transparent_unlit_hdr" : "ps_transparent_unlit_sdr",
            "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSpriteUnlitRgb",
            EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_transparent_unlit_hdr_rgb" : "ps_transparent_unlit_sdr",
            "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSpriteUnlitArgb",
            EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_transparent_unlit_hdr_argb" : "ps_transparent_unlit_sdr",
            "ps_5_0"),
        DisplayStaticSpriteShader(
            "SacredStaticSpriteUnlitAlphaMask",
            EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_transparent_unlit_hdr_alpha_mask" : "ps_transparent_unlit_sdr",
            "ps_5_0"),
        DisplayWaterShader(
            "SacredWater",
            EmbeddedResource_Shaders.SacredWater_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_water_hdr" : "ps_water_sdr",
            "ps_5_0"),
        StaticSpriteShadowVertexShader,
        StaticSpriteShadowPixelShader,
        DisplayShader(
            "SacredLightHalo",
            EmbeddedResource_Shaders.SacredLightHalo_hlsl,
            lightHaloVertexEntryPoint,
            "vs_5_0"),
        DisplayShader("SacredLightHalo", EmbeddedResource_Shaders.SacredLightHalo_hlsl, pixelEntryPoint, "ps_5_0"),
        ModelVertexShader,
        DisplayShader("SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl, pixelEntryPoint, "ps_5_0"),
        ModelShadowVertexShader,
        ModelShadowPixelShader,
        GroundShadowVertexShader,
        GroundShadowPixelShader,
        AnimatedModelVertexShader,
        DisplayShader("SacredAnimatedModel", EmbeddedResource_Shaders.SacredAnimatedModel_hlsl, pixelEntryPoint, "ps_5_0"),
        EffectModelVertexShader,
        DisplayShader("SacredEffectModel", EmbeddedResource_Shaders.SacredEffectModel_hlsl, pixelEntryPoint, "ps_5_0"),
        ItemParticleVertexShader,
        DisplayShader("SacredItemParticle", EmbeddedResource_Shaders.SacredItemParticle_hlsl, pixelEntryPoint, "ps_5_0"),
        DisplayShader(
            "SacredItemParticleRgb",
            EmbeddedResource_Shaders.SacredItemParticle_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_rgb" : "ps_sdr",
            "ps_5_0"),
        DisplayShader(
            "SacredItemParticleArgb",
            EmbeddedResource_Shaders.SacredItemParticle_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_argb" : "ps_sdr",
            "ps_5_0"),
        DisplayShader(
            "SacredItemParticleAlphaMask",
            EmbeddedResource_Shaders.SacredItemParticle_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_alpha_mask" : "ps_sdr",
            "ps_5_0"),
        ItemGlowVertexShader,
        DisplayShader("SacredItemGlow", EmbeddedResource_Shaders.SacredItemGlow_hlsl, pixelEntryPoint, "ps_5_0"),
        DisplayShader(
            "SacredItemGlowRgb",
            EmbeddedResource_Shaders.SacredItemGlow_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_rgb" : "ps_sdr",
            "ps_5_0"),
        DisplayShader(
            "SacredItemGlowArgb",
            EmbeddedResource_Shaders.SacredItemGlow_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_argb" : "ps_sdr",
            "ps_5_0"),
        DisplayShader(
            "SacredItemGlowAlphaMask",
            EmbeddedResource_Shaders.SacredItemGlow_hlsl,
            pixelEntryPoint == "ps_hdr" ? "ps_hdr_alpha_mask" : "ps_sdr",
            "ps_5_0"),
        InventoryUiVertexShader,
        DisplayShader("SacredInventoryUi", EmbeddedResource_Shaders.SacredInventoryUi_hlsl, pixelEntryPoint, "ps_5_0"));

    private static Dx12ShaderSource DisplayShader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string target) =>
        new(
            name,
            [
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(HdrCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())
            ],
            entryPoint,
            target);

    private static Dx12ShaderSource DisplayStaticSpriteShader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string target) =>
        new(
            name,
            [
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(HdrCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(SpriteCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(StaticSpriteCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())
            ],
            entryPoint,
            target);

    private static Dx12ShaderSource DisplayWaterShader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string target) =>
        new(
            name,
            [
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(HdrCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(SpriteCommon.GetResourceName()),
                () => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())
            ],
            entryPoint,
            target);
}

public sealed record Dx12ShaderSet(
    Dx12ShaderSource QuadWorldVertexShader,
    Dx12ShaderSource QuadWorldPixelShader,
    Dx12ShaderSource QuadScreenPixelShader,
    Dx12ShaderSource StaticSpriteVertexShader,
    Dx12ShaderSource StaticSpritePixelShader,
    Dx12ShaderSource TransparentStaticSpritePixelShader,
    Dx12ShaderSource UnlitStaticSpritePixelShader,
    Dx12ShaderSource TransparentUnlitStaticSpritePixelShader,
    Dx12ShaderSource TransparentUnlitParticleRgbPixelShader,
    Dx12ShaderSource TransparentUnlitParticleArgbPixelShader,
    Dx12ShaderSource TransparentUnlitParticleAlphaMaskPixelShader,
    Dx12ShaderSource WaterPixelShader,
    Dx12ShaderSource StaticSpriteShadowVertexShader,
    Dx12ShaderSource StaticSpriteShadowPixelShader,
    Dx12ShaderSource LightHaloVertexShader,
    Dx12ShaderSource LightHaloPixelShader,
    Dx12ShaderSource ModelVertexShader,
    Dx12ShaderSource ModelPixelShader,
    Dx12ShaderSource ModelShadowVertexShader,
    Dx12ShaderSource ModelShadowPixelShader,
    Dx12ShaderSource GroundShadowVertexShader,
    Dx12ShaderSource GroundShadowPixelShader,
    Dx12ShaderSource AnimatedModelVertexShader,
    Dx12ShaderSource AnimatedModelPixelShader,
    Dx12ShaderSource EffectModelVertexShader,
    Dx12ShaderSource EffectModelPixelShader,
    Dx12ShaderSource ItemParticleVertexShader,
    Dx12ShaderSource ItemParticlePixelShader,
    Dx12ShaderSource ItemParticleRgbPixelShader,
    Dx12ShaderSource ItemParticleArgbPixelShader,
    Dx12ShaderSource ItemParticleAlphaMaskPixelShader,
    Dx12ShaderSource ItemGlowVertexShader,
    Dx12ShaderSource ItemGlowPixelShader,
    Dx12ShaderSource ItemGlowRgbPixelShader,
    Dx12ShaderSource ItemGlowArgbPixelShader,
    Dx12ShaderSource ItemGlowAlphaMaskPixelShader,
    Dx12ShaderSource InventoryUiVertexShader,
    Dx12ShaderSource InventoryUiPixelShader);
