using System.Numerics;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

public sealed partial class SacredCamera
{
    private Vector2 _shakeWorldOffset;
    /// <summary>Presentation-only offset in native isometric pixels. Movement and network anchors stay unchanged.</summary>
    public void SetShakeOffset(Vector2 pixels)
    {
        var offset = IsometricProjection.IsoToWorld(pixels);
        if (_shakeWorldOffset == offset) return;
        _shakeWorldOffset = offset;
        RebuildMatrices();
    }
}
