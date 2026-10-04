namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    public void SetHdrUnlitColorMultiplier(float value) =>
        _graphics.SetHdrBrightnessSettings(_graphics.HdrBrightnessSettings with { UnlitColorMultiplier = value });

    public void SetHdrParticleColorMultiplier(float value) =>
        _graphics.SetHdrBrightnessSettings(_graphics.HdrBrightnessSettings with { ParticleColorMultiplier = value });
}
