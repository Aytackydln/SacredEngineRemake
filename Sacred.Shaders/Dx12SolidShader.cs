namespace Sacred.Shaders;

/// <summary>Early-depth variant for material coverage proven opaque by texture data.</summary>
internal static class Dx12SolidShader
{
    public static Dx12ShaderSource Create(Dx12ShaderSource source) =>
        new(source.Name + "Solid", [source.ReadAllBytes], source.EntryPoint + "_solid", source.Target);
}
