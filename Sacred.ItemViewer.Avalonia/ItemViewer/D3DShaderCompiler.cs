using System;
using Sacred.Shaders;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal static class D3DShaderCompiler
{
    public static ReadOnlyMemory<byte> Compile(Dx12ShaderSource shader) =>
        DxilShaderCompiler.Compile(shader);
}
