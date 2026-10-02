namespace Sacred.Shaders;

/// <summary>Selects the cutout entry point while preserving the shader's reloadable source.</summary>
internal static class Dx12OpaqueShader
{
    public static Dx12ShaderSource Create(Dx12ShaderSource source) =>
        new(source.Name + "Opaque", [source.ReadAllBytes], source.EntryPoint + "_opaque", source.Target);
}
