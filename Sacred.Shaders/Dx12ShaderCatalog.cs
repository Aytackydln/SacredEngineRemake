namespace Sacred.Shaders;

public static class Dx12ShaderCatalog
{
    private const EmbeddedResource_Shaders HdrCommon = EmbeddedResource_Shaders.HdrCommon_hlsl;
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
        DisplayShader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, "vs_main", "vs_5_0");
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

    // Sector composition is display-independent; SDR/HDR conversion happens later when the
    // completed sector texture is sampled by the world-quad shader.
    public static readonly Dx12ShaderSource TerrainComposeVertexShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "vs_main", "vs_5_0");
    public static readonly Dx12ShaderSource TerrainComposePixelShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "ps_main", "ps_5_0");

    public static readonly Dx12ShaderSet Sdr = CreateShaderSet("vs_sdr", "ps_sdr");
    public static readonly Dx12ShaderSet Hdr = CreateShaderSet("vs_hdr", "ps_hdr");

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
        StaticSpriteVertexShader,
        DisplayShader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, pixelEntryPoint, "ps_5_0"),
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
        ItemGlowVertexShader,
        DisplayShader("SacredItemGlow", EmbeddedResource_Shaders.SacredItemGlow_hlsl, pixelEntryPoint, "ps_5_0"),
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
}

public sealed record Dx12ShaderSet(
    Dx12ShaderSource QuadWorldVertexShader,
    Dx12ShaderSource QuadWorldPixelShader,
    Dx12ShaderSource StaticSpriteVertexShader,
    Dx12ShaderSource StaticSpritePixelShader,
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
    Dx12ShaderSource ItemGlowVertexShader,
    Dx12ShaderSource ItemGlowPixelShader,
    Dx12ShaderSource InventoryUiVertexShader,
    Dx12ShaderSource InventoryUiPixelShader);
