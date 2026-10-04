namespace Sacred.Engine.Graphics;

internal sealed partial class Dx12DeviceContext
{
    public bool UsesPackedHdrScene { get; private set; }

    /// <summary>The caller retires GPU work and pipelines before replacing scene targets.</summary>
    public void SetPackedHdrScene(bool packed)
    {
        UsesPackedHdrScene = packed;
        if (!IsHdrEnabled) return;
        DisposeBackBuffers();
        DisposeSceneColor();
        CreateBackBuffers();
        CreateSceneColor();
    }
}
