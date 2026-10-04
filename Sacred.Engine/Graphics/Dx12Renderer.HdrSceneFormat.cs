using Vortice.Direct3D12;
using Vortice.DXGI;
using Feature = Vortice.Direct3D12.Feature;

namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    public bool UsesPackedHdrScene => _graphics.UsesPackedHdrScene;

    /// <summary>Chooses smaller HDR targets at the cost of additive accumulation precision.</summary>
    public void SetPackedHdrScene(bool packed)
    {
        if (packed == UsesPackedHdrScene) return;
        if (packed)
        {
            var support = new FeatureDataFormatSupport { Format = Format.R11G11B10_Float };
            const FormatSupport1 required = FormatSupport1.Texture2D | FormatSupport1.RenderTarget |
                FormatSupport1.Blendable | FormatSupport1.ShaderLoad | FormatSupport1.ShaderSample;
            if (!_graphics.Device.CheckFeatureSupport(Feature.FormatSupport, ref support) ||
                (support.Support1 & required) != required)
            {
                EngineLog.WriteLine("Packed HDR scene format is unavailable; retaining FP16.");
                return;
            }
        }

        if (IsHdrEnabled)
        {
            _graphics.WaitForGpu(_releaseRetiredResources);
            DisposePipelineResources();
            _graphics.SetPackedHdrScene(packed);
            CreatePipeline();
            ResetFsr2History();
        }
        else
            _graphics.SetPackedHdrScene(packed);
        EngineLog.WriteLine($"HDR scene format: {(packed ? "R11G11B10_FLOAT (4 bytes; reduced blend precision)" : "RGBA16_FLOAT (8 bytes)")}.");
    }
}
