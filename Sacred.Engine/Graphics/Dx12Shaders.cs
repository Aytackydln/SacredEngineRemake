namespace Sacred.Engine.Graphics;

internal static class Dx12Shaders
{
    internal static readonly Dx12Shader QuadWorldVertexShader = new(
        "SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
        "vs_main", "vs_5_0"
    );
    internal static readonly Dx12Shader QuadWorldPixelShader = new(
        "SacredWorldQuad", EmbeddedResource_Shaders.SacredWorldQuad_hlsl,
        "ps_main", "ps_5_0"
    );

    internal static readonly Dx12Shader StaticSpriteVertexShader = new(
        "SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl,
        "vs_main", "vs_5_0"
    );
    internal static readonly Dx12Shader StaticSpritePixelShader = new(
        "SacredStaticSprite", EmbeddedResource_Shaders.SacredStaticSprite_hlsl,
        "ps_main", "ps_5_0"
    );

    internal static readonly Dx12Shader ModelVertexShader = new(
        "SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl,
        "vs_main", "vs_5_0"
    );
    internal static readonly Dx12Shader ModelPixelShader = new(
        "SacredModel", EmbeddedResource_Shaders.SacredModel_hlsl,
        "ps_main", "ps_5_0"
    );
}
