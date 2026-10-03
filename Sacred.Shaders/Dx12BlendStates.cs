using Vortice.Direct3D12;

namespace Sacred.Shaders;

public static class Dx12BlendStates
{
    /// <summary>Composites shader RGB that has not already been multiplied by coverage.</summary>
    public static BlendDescription StraightAlpha
    {
        get
        {
            var blend = BlendDescription.AlphaBlend;
            blend.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
            return blend;
        }
    }

    /// <summary>Composites premultiplied authored sprite RGB with its native coverage.</summary>
    public static BlendDescription HdrParticle
    {
        get
        {
            var blend = BlendDescription.AlphaBlend;
            blend.RenderTarget[0].SourceBlend = Blend.One;
            blend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
            blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
            blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
            return blend;
        }
    }

    /// <summary>Disables blending instead of using Vortice's enabled ONE/ZERO preset.</summary>
    public static BlendDescription Opaque
    {
        get
        {
            var blend = BlendDescription.Opaque;
            for (var index = 0; index < 8; index++)
                blend.RenderTarget[index].BlendEnable = false;
            return blend;
        }
    }
}
