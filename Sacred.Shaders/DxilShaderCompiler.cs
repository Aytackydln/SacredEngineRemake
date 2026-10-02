using System.Text;
using Vortice.Dxc;

namespace Sacred.Shaders;

/// <summary>Compiles embedded HLSL to validated DXIL for all Direct3D 12 consumers.</summary>
public static class DxilShaderCompiler
{
    public static ReadOnlyMemory<byte> Compile(Dx12ShaderSource shader)
    {
        var arguments = new List<string> { shader.Name, "-E", shader.EntryPoint, "-T", shader.Target, "-HV", "2018", "-O3" };
        if (shader.StrictFloat) arguments.Add("-Gis");
        using var result = DxcCompiler.Compile(
            Encoding.UTF8.GetString(shader.ReadAllBytes()),
            arguments.ToArray());
        if (result.GetStatus().Failure)
            throw new InvalidOperationException(
                $"Failed to compile {shader.Name}/{shader.EntryPoint} ({shader.Target}): {result.GetErrors()}");

        // Copy before disposing the result; pipeline creation can happen later.
        return result.GetObjectBytecodeMemory().ToArray();
    }
}
