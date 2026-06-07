namespace Sacred.Engine.Graphics;

internal static class Dx12Shaders
{
    private const EmbeddedResource_ShadersHdr HdrCommon = EmbeddedResource_ShadersHdr.HdrCommon_hlsl;

    internal static readonly Dx12ShaderSet Sdr = new(
        QuadWorldVertexShader: new Dx12Shader(
            "SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
            "vs_main", "vs_5_0"
        ),
        QuadWorldPixelShader: new Dx12Shader(
            "SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
            "ps_main", "ps_5_0"
        ),
        StaticSpriteVertexShader: new Dx12Shader(
            "SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl,
            "vs_main", "vs_5_0"
        ),
        StaticSpritePixelShader: new Dx12Shader(
            "SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl,
            "ps_main", "ps_5_0"
        ),
        ModelVertexShader: new Dx12Shader(
            "SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl,
            "vs_main", "vs_5_0"
        ),
        ModelPixelShader: new Dx12Shader(
            "SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl,
            "ps_main", "ps_5_0"
        )
    );

    internal static readonly Dx12ShaderSet Hdr = new(
        QuadWorldVertexShader: new Dx12Shader(
            "SacredWorldQuadHDR", EmbeddedResource_ShadersHdr.SacredWorldQuadHDR_hlsl,
            "vs_main", "vs_5_0",
            HdrCommon
        ),
        QuadWorldPixelShader: new Dx12Shader(
            "SacredWorldQuadHDR", EmbeddedResource_ShadersHdr.SacredWorldQuadHDR_hlsl,
            "ps_main", "ps_5_0",
            HdrCommon
        ),
        StaticSpriteVertexShader: new Dx12Shader(
            "SacredStaticSpriteHDR", EmbeddedResource_ShadersHdr.SacredStaticSpriteHDR_hlsl,
            "vs_main", "vs_5_0",
            HdrCommon
        ),
        StaticSpritePixelShader: new Dx12Shader(
            "SacredStaticSpriteHDR", EmbeddedResource_ShadersHdr.SacredStaticSpriteHDR_hlsl,
            "ps_main", "ps_5_0",
            HdrCommon
        ),
        ModelVertexShader: new Dx12Shader(
            "SacredModelHDR", EmbeddedResource_ShadersHdr.SacredModelHDR_hlsl,
            "vs_main", "vs_5_0",
            HdrCommon
        ),
        ModelPixelShader: new Dx12Shader(
            "SacredModelHDR", EmbeddedResource_ShadersHdr.SacredModelHDR_hlsl,
            "ps_main", "ps_5_0",
            HdrCommon
        )
    );
}

internal sealed record Dx12ShaderSet(
    Dx12Shader QuadWorldVertexShader,
    Dx12Shader QuadWorldPixelShader,
    Dx12Shader StaticSpriteVertexShader,
    Dx12Shader StaticSpritePixelShader,
    Dx12Shader ModelVertexShader,
    Dx12Shader ModelPixelShader);
