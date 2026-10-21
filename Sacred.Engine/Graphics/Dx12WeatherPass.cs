using System;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Native textured fog layers, rain streaks and textured snow, composed before UI.</summary>
internal sealed class Dx12WeatherPass : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly Dx12WeatherTextures _textures;
    public Dx12WeatherPass(Dx12DeviceContext graphics, AssetManager assets, Dx12TextureUploader uploader)
    {
        _device = graphics.Device;
        _textures = new(assets, graphics, uploader);
    }
    private ID3D12RootSignature? _root;
    private ID3D12PipelineState? _pipeline;
    private Format _format;
    public unsafe void Record(ID3D12GraphicsCommandList commands, SceneWeather weather,
        SacredCamera camera, Format format, int width, int height, Dx12FrameContext frame)
    {
        if (weather.Rain <= 0 && weather.Fog <= 0 && weather.Snow <= 0) return;
        if (!_textures.Prepare(commands, frame)) return;
        if (_pipeline is null || _format != format) CreatePipeline(format);
        var origin = IsometricProjection.WorldToIso(camera.ViewCenter);
        var constants = stackalloc float[24]
        {
            width, height, weather.Time, width / camera.ViewportZoom * camera.Zoom,
            weather.FogColour.X, weather.FogColour.Y, weather.FogColour.Z, weather.Fog,
            weather.RainColour.X, weather.RainColour.Y, weather.RainColour.Z, camera.ViewportZoom,
            origin.X - width / (2 * camera.ViewportZoom), origin.Y - height / (2 * camera.ViewportZoom), 0, weather.Snow,
            weather.SnowColour.X, weather.SnowColour.Y, weather.SnowColour.Z, height / camera.ViewportZoom * camera.Zoom,
            weather.FogScroll0.X, weather.FogScroll0.Y, weather.FogScroll1.X, weather.FogScroll1.Y
        };
        commands.SetGraphicsRootSignature(_root!);
        commands.SetPipelineState(_pipeline!);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.SetGraphicsRoot32BitConstants(0, 24, constants, 0);
        commands.SetGraphicsRootDescriptorTable(1, _textures.Fog);
        if (weather.Fog > 0) commands.DrawInstanced(6, 2, 0, 0);
        if (weather.Rain > 0)
        {
            constants[14] = 1;
            commands.SetGraphicsRoot32BitConstants(0, 24, constants, 0);
            var count = (uint)Math.Clamp(Math.Ceiling(weather.Rain * 3000 * width * (double)height / (1920 * 1080)), 1, 12000);
            commands.DrawInstanced(6, count, 0, 0);
        }
        if (weather.Snow > 0)
        {
            constants[14] = 2;
            commands.SetGraphicsRoot32BitConstants(0, 24, constants, 0);
            commands.SetGraphicsRootDescriptorTable(1, _textures.Snow);
            var count = (uint)Math.Clamp(Math.Ceiling(weather.Snow * 3000 * width * (double)height / (1920 * 1080)), 1, 12000);
            commands.DrawInstanced(6, count, 0, 0);
        }
    }
    private void CreatePipeline(Format format)
    {
        DisposePipeline();
        var parameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, 24), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable
            {
                Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0)]
            }, ShaderVisibility.Pixel)
        };
        var sampler = new StaticSamplerDescription(0, Filter.MinMagMipLinear,
            TextureAddressMode.Wrap, TextureAddressMode.Wrap, TextureAddressMode.Wrap,
            0, 1, ComparisonFunction.Never, StaticBorderColor.TransparentBlack,
            0, float.MaxValue, ShaderVisibility.Pixel, 0);
        var description = new RootSignatureDescription(RootSignatureFlags.None, parameters, [sampler]);
        _root = _device.CreateRootSignature(in description, RootSignatureVersion.Version1);
        try
        {
            var blend = BlendDescription.AlphaBlend;
            blend.RenderTarget[0].SourceBlend = Blend.One;
            blend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
            blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
            _pipeline = _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = _root,
                VertexShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.WeatherVertexShader),
                PixelShader = Dx12ShaderCompiler.CompileShader(Dx12ShaderCatalog.WeatherPixelShader),
                BlendState = blend,
                RasterizerState = RasterizerDescription.CullNone,
                DepthStencilState = DepthStencilDescription.None,
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = [format],
                DepthStencilFormat = Format.Unknown,
                SampleDescription = new SampleDescription(1, 0)
            });
            _format = format;
        }
        catch { DisposePipeline(); throw; }
    }
    public void DisposePipeline()
    {
        _pipeline?.Dispose(); _pipeline = null;
        _root?.Dispose(); _root = null;
    }
    public void Dispose()
    {
        DisposePipeline();
        _textures.Dispose();
    }
}
