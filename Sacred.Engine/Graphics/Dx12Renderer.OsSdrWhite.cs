namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    public OsSdrWhiteLevelInfo GetOsSdrWhiteLevel() => _graphics.GetOsSdrWhiteLevel(refresh: true);

    public bool ResetHdrBrightnessToOs()
    {
        var white = GetOsSdrWhiteLevel();
        if (!white.IsAvailable)
        {
            EngineLog.WriteLine($"HDR frame brightness unchanged: {white.Source}.");
            return false;
        }
        SetHdrBrightness(white.Nits);
        EngineLog.WriteLine($"HDR frame brightness reset to {white.Nits:0.##} nits from {white.Source}.");
        return true;
    }
}
