using System;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Original Gold weather art, loaded through the normal game texture source.</summary>
internal sealed class Dx12WeatherTextures : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _uploader;
    private readonly Task<TextureAsset>[] _loads;
    private readonly ID3D12Resource?[] _textures = new ID3D12Resource?[2];

    public Dx12WeatherTextures(AssetManager assets, Dx12DeviceContext graphics, Dx12TextureUploader uploader)
    {
        (_graphics, _uploader) = (graphics, uploader);
        // Gold 0x419390 / 0x4196E2. Do not substitute generic smoke or snow textures.
        _loads = [assets.LoadTextureAsync("WEATHER_FOG01.TGA"), assets.LoadTextureAsync("PARTICLE_SPARK03.TGA")];
    }

    public GpuDescriptorHandle Fog => _graphics.SrvGpuHandle(Dx12DescriptorLayout.FirstWeatherTexture);
    public GpuDescriptorHandle Snow => _graphics.SrvGpuHandle(Dx12DescriptorLayout.FirstWeatherTexture + 1);

    public bool Prepare(ID3D12GraphicsCommandList commands, Dx12FrameContext frame)
    {
        var ready = true;
        for (var i = 0; i < _textures.Length; i++)
        {
            if (_textures[i] is not null) continue;
            if (!_loads[i].IsCompleted) { ready = false; continue; }
            var asset = _loads[i].GetAwaiter().GetResult();
            _textures[i] = _uploader.UploadRgbaTexture(commands, asset.Width, asset.Height, asset.Rgba8, frame.TransientResources);
            _uploader.CreateShaderResourceView(_textures[i]!, _graphics.SrvCpuHandle(Dx12DescriptorLayout.FirstWeatherTexture + i));
            EngineLog.WriteLine($"Weather texture loaded: {asset.Name}; {asset.Width}x{asset.Height}.");
        }
        return ready;
    }

    public void Dispose()
    {
        foreach (var texture in _textures) texture?.Dispose();
    }
}
