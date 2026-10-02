namespace Sacred.Engine.Graphics;

internal static class RenderScalingModeResolver
{
    public static RenderScalingMode Resolve(RenderScalingMode mode,
        int renderWidth, int renderHeight, int outputWidth, int outputHeight)
    {
        if (mode != RenderScalingMode.Fsr2Lanczos2)
            return mode;

        // Any shrinking axis needs a low-pass filter, including mixed aspect ratios.
        return renderWidth > outputWidth || renderHeight > outputHeight
            ? RenderScalingMode.Lanczos2
            : RenderScalingMode.Fsr2;
    }
}
