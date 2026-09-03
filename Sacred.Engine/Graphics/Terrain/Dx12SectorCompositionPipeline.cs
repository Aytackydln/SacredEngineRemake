using System.Runtime.InteropServices;
using Sacred.Engine.Rendering;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Terrain;

internal static class Dx12SectorCompositionPipeline
{
    public static Dx12SectorCompositionPipelines Create(
        ID3D12Device device,
        Format outputFormat)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 2), ShaderVisibility.Vertex),
            new RootParameter(RootParameterType.ShaderResourceView, new RootDescriptor(0, 0), ShaderVisibility.Vertex),
            new RootParameter(
                new RootDescriptorTable
                {
                    Ranges = [new DescriptorRange(
                        DescriptorRangeType.ShaderResourceView,
                        2,
                        1,
                        0,
                        0)]
                },
                ShaderVisibility.Pixel)
        };
        var rootDescription = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            rootParameters,
            []);
        var rootSignature = device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
        var vertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.TerrainComposeVertexShader);
        var pixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.TerrainComposePixelShader);
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [outputFormat],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0)
        };

        var straightAlphaBlend = BlendDescription.AlphaBlend;
        straightAlphaBlend.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
        straightAlphaBlend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
        straightAlphaBlend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        straightAlphaBlend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        description.BlendState = straightAlphaBlend;
        var basePipeline = device.CreateGraphicsPipelineState(description);

        var premultipliedBlend = BlendDescription.AlphaBlend;
        premultipliedBlend.RenderTarget[0].SourceBlend = Blend.One;
        premultipliedBlend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
        premultipliedBlend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        premultipliedBlend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        description.BlendState = premultipliedBlend;
        return new Dx12SectorCompositionPipelines(
            rootSignature,
            basePipeline,
            device.CreateGraphicsPipelineState(description));
    }
}

internal sealed record Dx12SectorCompositionPipelines(
    ID3D12RootSignature RootSignature,
    ID3D12PipelineState Base,
    ID3D12PipelineState Cover);

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTerrainTileInstance
{
    public GpuTerrainTileInstance(
        float destinationX,
        float destinationY,
        float primarySourceX,
        float primarySourceY,
        float secondarySourceX,
        float secondarySourceY,
        uint primaryTextureIndex,
        uint secondaryTextureIndex,
        uint flags,
        TerrainTileSurface surface)
    {
        DestinationX = destinationX;
        DestinationY = destinationY;
        PrimarySourceX = primarySourceX;
        PrimarySourceY = primarySourceY;
        SecondarySourceX = secondarySourceX;
        SecondarySourceY = secondarySourceY;
        PrimaryTextureIndex = primaryTextureIndex;
        SecondaryTextureIndex = secondaryTextureIndex;
        Flags = flags;
        var bakedLight = surface.BakedLight;
        PackedBakedLight = bakedLight.SouthWest |
                           (uint)bakedLight.NorthWest << 8 |
                           (uint)bakedLight.NorthEast << 16 |
                           (uint)bakedLight.SouthEast << 24;
        var elevation = surface.VisualElevation;
        VisualElevationSouthWest = elevation.SouthWest;
        VisualElevationNorthWest = elevation.NorthWest;
        VisualElevationNorthEast = elevation.NorthEast;
        VisualElevationSouthEast = elevation.SouthEast;
    }

    public readonly float DestinationX;
    public readonly float DestinationY;
    public readonly float PrimarySourceX;
    public readonly float PrimarySourceY;
    public readonly float SecondarySourceX;
    public readonly float SecondarySourceY;
    public readonly uint PrimaryTextureIndex;
    public readonly uint SecondaryTextureIndex;
    public readonly uint Flags;
    public readonly uint PackedBakedLight;
    public readonly float VisualElevationSouthWest;
    public readonly float VisualElevationNorthWest;
    public readonly float VisualElevationNorthEast;
    public readonly float VisualElevationSouthEast;
}
