using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native stored timer, strict retirement, Mana extension and rotating ground-layer geometry.</summary>
public sealed class SacredLayeredFlareFxPlayback
{
    private SacredTimedFlareFxStateLayout _state;
    public SacredLayeredFlareFxPlayback(SacredLayeredFlareFxDefinition definition)
    {
        Definition = definition; Restart();
    }
    public SacredLayeredFlareFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public uint StartNotification { get; private set; }
    public SacredTimedFlareFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredTimedFlareFxStateLayout state) { _state = state; StartNotification = 0; }
    public void Restart()
    {
        _state = new() { Active = 1, Lifetime = Definition.InitialLifetime, Auxiliary = Definition.InitialAuxiliary };
        StartNotification = Definition.StartNotification;
    }
    /// <summary>Mana's zero-art event extends from current age and leaves its rotation timer intact.</summary>
    public void ExtendDuration(uint durationUnits)
    {
        if (!Definition.CanExtendDuration) throw new InvalidOperationException("This controller has no native timer-extension branch.");
        _state.Lifetime = (float)(durationUnits * (double)Definition.DurationScale + _state.ElapsedTime);
        _state.Active = 1; StartNotification = Definition.StartNotification;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)elapsed;
        if (elapsed > _state.Lifetime) _state.Active = 0;
    }
    public Matrix4x4 LayerMatrix(int index)
    {
        var layer = Definition.Layers[index];
        var angle = (float)(_state.ElapsedTime * (double)layer.RotationRate);
        return Matrix4x4.CreateRotationZ(-angle) * Matrix4x4.CreateTranslation(layer.Offset);
    }
    public Vector3 LayerVertex(int index, int corner)
    {
        if ((uint)corner >= 4) throw new ArgumentOutOfRangeException(nameof(corner));
        var size = Definition.Layers[index].HalfSize;
        return Vector3.Transform(new(corner < 2 ? size : -size, (corner % 2 * 2 - 1) * size, 0), LayerMatrix(index));
    }
    public static Vector2 TextureCoordinate(int corner) => new(corner < 2 ? 1 : 0, corner % 2);
}
