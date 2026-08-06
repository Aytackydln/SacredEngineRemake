namespace Sacred.Shaders;

public static class Dx12ShaderCatalog
{
    private const EmbeddedResource_ShadersHdr HdrCommon = EmbeddedResource_ShadersHdr.HdrCommon_hlsl;
    private static readonly Dx12ShaderSource ModelShadowVertexShader =
        Shader("SacredModelShadow", EmbeddedResource_Shaders.SacredModelShadow_hlsl, "vs_main", "vs_5_0");
    private static readonly Dx12ShaderSource ModelShadowPixelShader =
        Shader("SacredModelShadow", EmbeddedResource_Shaders.SacredModelShadow_hlsl, "ps_main", "ps_5_0");

    // Sector composition is display-independent; SDR/HDR conversion happens later when the
    // completed sector texture is sampled by the world-quad shader.
    public static readonly Dx12ShaderSource TerrainComposeVertexShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "vs_main", "vs_5_0");
    public static readonly Dx12ShaderSource TerrainComposePixelShader =
        Shader("SacredTerrainCompose", EmbeddedResource_Shaders.SacredTerrainCompose_hlsl, "ps_main", "ps_5_0");

    public static readonly Dx12ShaderSet Sdr = new(
        QuadWorldVertexShader: Shader("SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl, "vs_main", "vs_5_0"),
        QuadWorldPixelShader: Shader("SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl, "ps_main", "ps_5_0"),
        StaticSpriteVertexShader: Shader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, "vs_main", "vs_5_0"),
        StaticSpritePixelShader: Shader("SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl, "ps_main", "ps_5_0"),
        ModelVertexShader: Shader("SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl, "vs_main", "vs_5_0"),
        ModelPixelShader: Shader("SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl, "ps_main", "ps_5_0"),
        ModelShadowVertexShader: ModelShadowVertexShader,
        ModelShadowPixelShader: ModelShadowPixelShader,
        AnimatedModelVertexShader: Shader("SacredAnimatedModel", EmbeddedResource_Shaders.SacredAnimatedModel_hlsl, "vs_main", "vs_5_0"),
        AnimatedModelPixelShader: Shader("SacredAnimatedModel", EmbeddedResource_Shaders.SacredAnimatedModel_hlsl, "ps_main", "ps_5_0"),
        EffectModelVertexShader: Shader("SacredEffectModel", EmbeddedResource_Shaders.SacredEffectModel_hlsl, "vs_main", "vs_5_0"),
        EffectModelPixelShader: Shader("SacredEffectModel", EmbeddedResource_Shaders.SacredEffectModel_hlsl, "ps_main", "ps_5_0"),
        ItemParticleVertexShader: Shader("SacredItemParticle", EmbeddedResource_Shaders.SacredItemParticle_hlsl, "vs_main", "vs_5_0"),
        ItemParticlePixelShader: Shader("SacredItemParticle", EmbeddedResource_Shaders.SacredItemParticle_hlsl, "ps_main", "ps_5_0"),
        ItemGlowVertexShader: Shader("SacredItemGlow", EmbeddedResource_Shaders.SacredItemGlow_hlsl, "vs_main", "vs_5_0"),
        ItemGlowPixelShader: Shader("SacredItemGlow", EmbeddedResource_Shaders.SacredItemGlow_hlsl, "ps_main", "ps_5_0"),
        InventoryUiVertexShader: Shader("SacredInventoryUi", EmbeddedResource_Shaders.SacredInventoryUi_hlsl, "vs_main", "vs_5_0"),
        InventoryUiPixelShader: Shader("SacredInventoryUi", EmbeddedResource_Shaders.SacredInventoryUi_hlsl, "ps_main", "ps_5_0")
    );

    public static readonly Dx12ShaderSet Hdr = new(
        QuadWorldVertexShader: Shader("SacredWorldQuadHDR", EmbeddedResource_ShadersHdr.SacredWorldQuadHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        QuadWorldPixelShader: Shader("SacredWorldQuadHDR", EmbeddedResource_ShadersHdr.SacredWorldQuadHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        StaticSpriteVertexShader: Shader("SacredStaticSpriteHDR", EmbeddedResource_ShadersHdr.SacredStaticSpriteHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        StaticSpritePixelShader: Shader("SacredStaticSpriteHDR", EmbeddedResource_ShadersHdr.SacredStaticSpriteHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        ModelVertexShader: Shader("SacredModelHDR", EmbeddedResource_ShadersHdr.SacredModelHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        ModelPixelShader: Shader("SacredModelHDR", EmbeddedResource_ShadersHdr.SacredModelHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        ModelShadowVertexShader: ModelShadowVertexShader,
        ModelShadowPixelShader: ModelShadowPixelShader,
        AnimatedModelVertexShader: Shader("SacredAnimatedModelHDR", EmbeddedResource_ShadersHdr.SacredAnimatedModelHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        AnimatedModelPixelShader: Shader("SacredAnimatedModelHDR", EmbeddedResource_ShadersHdr.SacredAnimatedModelHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        EffectModelVertexShader: Shader("SacredEffectModelHDR", EmbeddedResource_ShadersHdr.SacredEffectModelHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        EffectModelPixelShader: Shader("SacredEffectModelHDR", EmbeddedResource_ShadersHdr.SacredEffectModelHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        ItemParticleVertexShader: Shader("SacredItemParticleHDR", EmbeddedResource_ShadersHdr.SacredItemParticleHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        ItemParticlePixelShader: Shader("SacredItemParticleHDR", EmbeddedResource_ShadersHdr.SacredItemParticleHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        ItemGlowVertexShader: Shader("SacredItemGlowHDR", EmbeddedResource_ShadersHdr.SacredItemGlowHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        ItemGlowPixelShader: Shader("SacredItemGlowHDR", EmbeddedResource_ShadersHdr.SacredItemGlowHDR_hlsl, "ps_main", "ps_5_0", HdrCommon),
        InventoryUiVertexShader: Shader("SacredInventoryUiHDR", EmbeddedResource_ShadersHdr.SacredInventoryUiHDR_hlsl, "vs_main", "vs_5_0", HdrCommon),
        InventoryUiPixelShader: Shader("SacredInventoryUiHDR", EmbeddedResource_ShadersHdr.SacredInventoryUiHDR_hlsl, "ps_main", "ps_5_0", HdrCommon)
    );

    /// <summary>Raised after the embedded shader assembly is rebuilt.</summary>
    public static event Action? Reloaded;

    static Dx12ShaderCatalog() => EmbeddedShaderAssemblyReloader.WatchForRebuilds(() => Reloaded?.Invoke());

    private static Dx12ShaderSource Shader(
        string name,
        EmbeddedResource_Shaders resource,
        string entryPoint,
        string target) =>
        new(name, [() => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())], entryPoint, target);

    private static Dx12ShaderSource Shader(
        string name,
        EmbeddedResource_ShadersHdr resource,
        string entryPoint,
        string target,
        EmbeddedResource_ShadersHdr? header = null) =>
        new(
            name,
            header.HasValue
                ? [
                    () => EmbeddedShaderAssemblyReloader.ReadAllBytes(header.Value.GetResourceName()),
                    () => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())
                ]
                : [() => EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName())],
            entryPoint,
            target);

}

public sealed record Dx12ShaderSet(
    Dx12ShaderSource QuadWorldVertexShader,
    Dx12ShaderSource QuadWorldPixelShader,
    Dx12ShaderSource StaticSpriteVertexShader,
    Dx12ShaderSource StaticSpritePixelShader,
    Dx12ShaderSource ModelVertexShader,
    Dx12ShaderSource ModelPixelShader,
    Dx12ShaderSource ModelShadowVertexShader,
    Dx12ShaderSource ModelShadowPixelShader,
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
