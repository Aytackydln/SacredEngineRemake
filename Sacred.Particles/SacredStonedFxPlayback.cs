using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed class SacredStonedFxPlayback
{
    private SacredStonedFxStateLayout _state;
    private uint _randomState;
    private readonly Func<int> _nextRandom;

    public SacredStonedFxPlayback(SacredStonedFxDefinition definition, uint seed = 1, Func<int>? nextRandom = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _randomState = seed; _nextRandom = nextRandom ?? NextRandom;
        Restart();
    }
    public SacredStonedFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public uint Notification { get; private set; }
    public SacredStonedFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredStonedFxStateLayout state) => _state = state;

    /// <summary>Null selects the native default branch. Otherwise supply the resolved combat-art duration.
    /// Reinitialization preserves unknown bytes and draws a fresh heading.</summary>
    public void Restart(float? combatDuration = null)
    {
        _state.ElapsedTime = 0; _state.Active = 1;
        _state.Duration = combatDuration ?? Definition.DefaultDuration;
        _state.Heading = (float)(_nextRandom() * (double)Definition.RandomScale * Definition.FullTurn);
        Notification = Definition.StartNotification;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var elapsed = _state.ElapsedTime + (double)seconds;
        _state.ElapsedTime = (float)elapsed;
        if (elapsed > _state.Duration) _state.Active = 0;
    }
    public Matrix4x4 ModelMatrix(Vector3 position = default) =>
        Matrix4x4.CreateRotationZ(-_state.Heading) * Matrix4x4.CreateTranslation(position);
    private int NextRandom()
    {
        _randomState = unchecked(_randomState * 214013u + 2531011u);
        return (int)((_randomState >> 16) & 32767);
    }
}
