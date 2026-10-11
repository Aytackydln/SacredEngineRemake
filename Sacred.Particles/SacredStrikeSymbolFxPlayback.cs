using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native strict timers, single random change per update and retirement notification.</summary>
public sealed class SacredStrikeSymbolFxPlayback
{
    private SacredCrusaderStrengthFxStateLayout _state;
    private float _elapsedRate;
    private uint _color;
    public SacredStrikeSymbolFxPlayback(SacredStrikeSymbolFxDefinition definition) { Definition = definition; Restart(); }
    public SacredStrikeSymbolFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public int TextureFrame => _state.TextureFrame;
    public uint FinishNotification { get; private set; }
    public uint FinishArgument => FinishNotification == 0 ? 0 : Definition.FinishArgument;
    public string? FinishTexture => FinishNotification == 0 ? null : Definition.FinishTexture;
    public uint FlareColor => Definition.UsesSavedColor ? _color | 0xFF000000 : Definition.FlareColor;
    public uint SurfaceColor(int corner) => Definition.UsesSavedColor
        ? _color | (corner < 2 ? 0xFF000000u : 0) : Definition.SurfaceVertices[corner].Color;
    public void Restart()
    {
        _state = new() { Active = 1 }; _elapsedRate = Definition.InitialElapsedRate;
        _color = Definition.InitialColor; FinishNotification = 0;
    }
    public SacredCrusaderStrengthFxStateLayout CaptureStrengthState() => _state;
    public SacredSpeedUpFxStateLayout CaptureSpeedUpState() => new()
    {
        ElapsedTime = _state.ElapsedTime, FrameTime = _state.FrameTime, ElapsedRate = _elapsedRate,
        Active = _state.Active, Unknown0D = _state.Unknown09, Unknown0E = _state.Unknown0A, TextureFrame = _state.TextureFrame
    };
    public void RestoreState(in SacredCrusaderStrengthFxStateLayout state) { _state = state; FinishNotification = 0; }
    public SacredSuperfluidFxStateLayout CaptureSuperfluidState() => new()
    {
        ElapsedTime = _state.ElapsedTime, FrameTime = _state.FrameTime, ElapsedRate = _elapsedRate,
        Active = _state.Active, Unknown0D = _state.Unknown09, Unknown0E = _state.Unknown0A, TextureFrame = _state.TextureFrame
    };
    public void RestoreState(in SacredSuperfluidFxStateLayout state)
    {
        var common = new SacredSpeedUpFxStateLayout { ElapsedTime = state.ElapsedTime, FrameTime = state.FrameTime,
            ElapsedRate = state.ElapsedRate, Active = state.Active, Unknown0D = state.Unknown0D,
            Unknown0E = state.Unknown0E, TextureFrame = state.TextureFrame };
        RestoreState(in common);
    }
    public SacredShieldedFxStateLayout CaptureShieldedState() => new()
    {
        ElapsedTime = _state.ElapsedTime, FrameTime = _state.FrameTime, ElapsedRate = _elapsedRate,
        Active = _state.Active, Unknown0D = _state.Unknown09, Unknown0E = _state.Unknown0A,
        TextureFrame = _state.TextureFrame, Color = _color
    };
    public void RestoreState(in SacredShieldedFxStateLayout state)
    {
        var common = new SacredSpeedUpFxStateLayout { ElapsedTime = state.ElapsedTime, FrameTime = state.FrameTime,
            ElapsedRate = state.ElapsedRate, Active = state.Active, Unknown0D = state.Unknown0D,
            Unknown0E = state.Unknown0E, TextureFrame = state.TextureFrame };
        RestoreState(in common); _color = state.Color;
    }
    public void RestoreState(in SacredSpeedUpFxStateLayout state)
    {
        _state = new() { ElapsedTime = state.ElapsedTime, FrameTime = state.FrameTime, Active = state.Active,
            Unknown09 = state.Unknown0D, Unknown0A = state.Unknown0E, TextureFrame = state.TextureFrame };
        _elapsedRate = state.ElapsedRate; FinishNotification = 0;
    }
    public void Advance(float seconds, Func<int> nextRandom)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + seconds * (Definition.UsesElapsedRate ? (double)_elapsedRate : 1);
        _state.ElapsedTime = (float)elapsed;
        if (elapsed > Definition.Lifetime) { _state.Active = 0; FinishNotification = Definition.FinishNotification; }
        var frameTime = _state.FrameTime + (double)seconds;
        _state.FrameTime = (float)frameTime;
        if (frameTime > Definition.FrameInterval)
        {
            _state.FrameTime = (float)(frameTime - Definition.FrameInterval);
            _state.TextureFrame = nextRandom() % Definition.Textures.Count;
        }
    }
    public Vector2 SurfaceTextureCoordinate(int corner, int? frame = null)
    {
        var selected = frame ?? TextureFrame;
        return new(Definition.SurfaceVertices[corner].TextureCoordinate.X,
            selected % 2 * .5f + (corner % 2 == 0 ? .5f : 0));
    }
    public Vector3 FlareVertex(int corner) => new(corner < 2 ? -Definition.FlareHalfSize : Definition.FlareHalfSize,
        (corner % 2 * 2 - 1) * Definition.FlareHalfSize, 0);
    public static Vector2 FlareTextureCoordinate(int corner) => new(corner / 2, 1 - corner % 2);
}
