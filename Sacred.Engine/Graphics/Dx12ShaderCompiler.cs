using System;
using Sacred.Shaders;

namespace Sacred.Engine.Graphics;

public static class Dx12ShaderCompiler
{
    internal static ReadOnlyMemory<byte> CompileShader(Dx12ShaderSource shader) =>
        DxilShaderCompiler.Compile(shader);
}
