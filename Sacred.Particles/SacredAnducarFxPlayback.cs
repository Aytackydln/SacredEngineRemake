using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Original per-model delay, grow, hold and shrink controller with stored random geometry.</summary>
public sealed class SacredAnducarFxPlayback
{
    private SacredAnducarFxStateLayout _state;
    public SacredAnducarFxPlayback(SacredAnducarFxDefinition definition, Func<int> random, Vector3 endpointDifference)
    {
        Definition = definition;
        Restart(random, endpointDifference);
    }
    public SacredAnducarFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public uint StartNotification { get; private set; }
    public SacredAnducarFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredAnducarFxStateLayout state)
    {
        if (state.Count > SacredAnducarFxStateLayout.MaximumCount) throw new ArgumentOutOfRangeException(nameof(state));
        for (var index = 0; index < state.Count; index++)
            if (state.ModelSelectors[index] >= Definition.ModelNames.Count) throw new ArgumentException("Unknown saved Anducar model selector.", nameof(state));
        _state = state;
        StartNotification = 0;
    }

    public void Restart(Func<int> random, Vector3 endpointDifference)
    {
        ArgumentNullException.ThrowIfNull(random);
        _state = new() { Count = Definition.Count, Active = 1 };
        StartNotification = 0;
        for (var index = 0; index < _state.Count; index++)
        {
            _state.HoldTimes[index] = Definition.HoldSeconds;
            _state.Alive[index] = 1;
            _state.Scales[index] = (float)(random() * (double)Definition.RandomScale * Definition.ScaleWidth + Definition.ScaleOrigin);
            _state.Angles[index] = (float)(random() * (double)Definition.RandomScale * Definition.FullTurn);
            var y = (float)(random() * (double)Definition.RandomScale * Definition.OffsetWidth - Definition.OffsetOrigin);
            var x = (float)(random() * (double)Definition.RandomScale * Definition.OffsetWidth - Definition.OffsetOrigin);
            _state.Offsets[index] = new(x, y, 0);
            var dx = endpointDifference.X + (double)x;
            var dy = endpointDifference.Y + (double)y;
            var dz = (double)endpointDifference.Z;
            _state.Delays[index] = (float)(Math.Sqrt(dx * dx + dy * dy + dz * dz) * Definition.DelayPerUnit);
            _state.ModelSelectors[index] = (byte)(random() % Definition.ModelNames.Count);
        }
    }

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        for (var index = 0; index < _state.Count; index++)
        {
            switch (_state.Stages[index])
            {
                case 0:
                    var delay = _state.Delays[index] - (double)seconds;
                    _state.Delays[index] = (float)delay;
                    if (delay <= 0) { _state.Stages[index] = 1; StartNotification = Definition.StartNotification; }
                    break;
                case 1:
                    _state.ElapsedTimes[index] = (float)(_state.ElapsedTimes[index] + (double)seconds);
                    var growth = _state.Envelopes[index] + seconds * (double)Definition.EnvelopeRate;
                    _state.Envelopes[index] = (float)growth;
                    if (growth > 1) { _state.Envelopes[index] = 1; _state.Stages[index] = 2; }
                    break;
                case 2:
                    _state.ElapsedTimes[index] = (float)(_state.ElapsedTimes[index] + (double)seconds);
                    var hold = _state.HoldTimes[index] - (double)seconds;
                    _state.HoldTimes[index] = (float)hold;
                    if (hold < 0) { _state.HoldTimes[index] = 0; _state.Stages[index] = 3; }
                    break;
                case 3:
                    _state.ElapsedTimes[index] = (float)(_state.ElapsedTimes[index] + (double)seconds);
                    var shrink = _state.Envelopes[index] - seconds * (double)Definition.EnvelopeRate;
                    _state.Envelopes[index] = (float)shrink;
                    if (shrink < 0) { _state.Envelopes[index] = 0; _state.Alive[index] = 0; }
                    break;
            }
        }
        _state.Active = 0;
        for (var index = 0; index < _state.Count; index++)
            if (_state.Alive[index] != 0) { _state.Active = 1; break; }
    }

    public bool DrawsModel(int index) => _state.Stages[index] is 1 or 2 or 3;
    public int ModelSelector(int index) => _state.ModelSelectors[index];
    public Matrix4x4 ModelMatrix(int index)
    {
        var scale = (float)(_state.Scales[index] * (double)_state.Envelopes[index]);
        var horizontal = (float)(_state.Scales[index] * (double)_state.Envelopes[index] * Definition.HorizontalScaleFactor);
        return Matrix4x4.CreateScale(horizontal, horizontal, scale) * Matrix4x4.CreateRotationZ(-_state.Angles[index]) *
            Matrix4x4.CreateTranslation(_state.Offsets[index]);
    }
}
