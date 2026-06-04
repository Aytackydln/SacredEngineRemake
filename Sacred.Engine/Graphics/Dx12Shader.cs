namespace Sacred.Engine.Graphics;

public class Dx12Shader(
    string name,
    EmbeddedResource_Shaders resource,
    string entryPoint,
    string shaderTarget
)
{
    public string Name { get; } = name;
    public EmbeddedResource_Shaders Resource { get; } = resource;

    public string ShaderEntry { get; } = entryPoint;
    public string ShaderTarget { get; } = shaderTarget;
}