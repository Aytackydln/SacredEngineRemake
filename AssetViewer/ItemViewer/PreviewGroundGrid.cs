using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Dxc;
using Vortice.DXGI;

namespace AssetViewer.ItemViewer;

/// <summary>A viewer-only reference plane with antialiased lines and a radial fade.</summary>
internal sealed class PreviewGroundGrid : IDisposable
{
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipeline;

    public PreviewGroundGrid(ID3D12Device device, Format colorFormat, Format depthFormat)
    {
        using var stream = typeof(PreviewGroundGrid).Assembly.GetManifestResourceStream(
            "AssetViewer.ItemViewer.PreviewGroundGrid.hlsl")!;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var description = new RootSignatureDescription(RootSignatureFlags.None,
            [new RootParameter(new RootConstants(0, 0, 24), ShaderVisibility.All)]);
        _rootSignature = device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        try
        {
            var depth = DepthStencilDescription.Default;
            depth.DepthWriteMask = DepthWriteMask.Zero;
            _pipeline = device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = _rootSignature,
                VertexShader = Compile(source, "vs_main", "vs_6_6"),
                PixelShader = Compile(source, "ps_main", "ps_6_6"),
                BlendState = BlendDescription.NonPremultiplied,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = depth,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = [colorFormat],
                DepthStencilFormat = depthFormat,
                SampleDescription = new SampleDescription(1, 0),
                SampleMask = uint.MaxValue
            });
        }
        catch
        {
            _rootSignature.Dispose();
            throw;
        }
    }

    public unsafe void Record(ID3D12GraphicsCommandList commands, Matrix4x4 transform,
        float cellSize, float radius)
    {
        // All geometry is in world units; zoom affects it through projection only.
        var constants = stackalloc float[24];
        MemoryMarshal.Write(new Span<byte>(constants, 64), in transform);
        constants[16] = radius;
        constants[17] = cellSize;
        constants[18] = radius * 0.15f;
        constants[19] = radius;
        constants[20] = 0.65f; // Mostly opaque line centers near the object.
        constants[21] = 0.5f; // World line width scales naturally with the cells.
        constants[22] = 0;
        constants[23] = 0;
        commands.SetGraphicsRootSignature(_rootSignature);
        commands.SetPipelineState(_pipeline);
        commands.SetGraphicsRoot32BitConstants(0, 24, constants, 0);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.DrawInstanced(6, 1, 0, 0);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        _rootSignature.Dispose();
    }

    private static ReadOnlyMemory<byte> Compile(string source, string entry, string target)
    {
        using var result = DxcCompiler.Compile(source,
            ["PreviewGroundGrid.hlsl", "-E", entry, "-T", target, "-HV", "2018", "-O3"]);
        if (result.GetStatus().Failure)
            throw new InvalidOperationException($"Ground grid shader {entry}: {result.GetErrors()}");
        return result.GetObjectBytecodeMemory().ToArray();
    }
}
