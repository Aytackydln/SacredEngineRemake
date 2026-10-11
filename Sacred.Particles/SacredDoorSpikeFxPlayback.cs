using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native grow/hold/release/shrink controller; renders all six authored model matrices.</summary>
public sealed class SacredDoorSpikeFxPlayback
{
    private SacredDoorSpikeFxStateLayout _state;

    public SacredDoorSpikeFxPlayback(SacredDoorSpikeFxDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.DrawCount is < 1 or > SacredDoorSpikeFxStateLayout.StoredCount)
            throw new ArgumentOutOfRangeException(nameof(definition));
        Definition = definition;
        _state = definition.InitialState;
    }

    public SacredDoorSpikeFxDefinition Definition { get; }
    public bool Active => _state.Active != 0;
    public byte Phase => _state.Phase;
    public ushort ControlFlags { get; set; }
    public SacredDoorSpikeFxStateLayout CaptureState() => _state;
    public void RestoreState(in SacredDoorSpikeFxStateLayout state) => _state = state;
    public void RequestRelease() => ControlFlags |= Definition.ReleaseFlag;

    /// <summary>Native reinitialization preserves elapsed time, padding and pending base flags.</summary>
    public void Restart()
    {
        var elapsed = _state.ElapsedTime;
        var unknown005 = _state.Unknown005; var unknown006 = _state.Unknown006;
        var unknown035 = _state.Unknown035; var unknown036 = _state.Unknown036;
        _state = Definition.InitialState;
        _state.ElapsedTime = elapsed;
        _state.Unknown005 = unknown005; _state.Unknown006 = unknown006;
        _state.Unknown035 = unknown035; _state.Unknown036 = unknown036;
    }

    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _state.ElapsedTime = (float)(_state.ElapsedTime + (double)seconds);
        switch (_state.Phase)
        {
            case 0:
                var grow = _state.Envelope + (double)seconds;
                _state.Envelope = (float)grow;
                // x87 compares the extended value before rounding the saved float.
                if (grow > 1) { _state.Envelope = 1; _state.Phase = 1; }
                break;
            case 1:
                if ((ControlFlags & Definition.ReleaseFlag) != 0)
                {
                    ControlFlags &= (ushort)~Definition.ReleaseFlag;
                    _state.Phase = 2;
                }
                break;
            case 2:
                var shrink = _state.Envelope - (double)seconds;
                _state.Envelope = (float)shrink;
                if (shrink < 0) { _state.Envelope = 0; _state.Active = 0; }
                break;
        }
    }

    /// <summary>Native row-vector Y tilt followed by clockwise Z rotation, uniform scale and XYZ placement.</summary>
    public Matrix4x4 ModelMatrix(int index, Vector3 position = default)
    {
        if ((uint)index >= Definition.DrawCount) throw new ArgumentOutOfRangeException(nameof(index));
        var heading = (index & 1) == 0 ? Definition.EvenHeading : Definition.OddHeading;
        var scale = (float)(_state.Scales[index] * (double)_state.Envelope);
        var tilt = Matrix4x4.CreateRotationY(Definition.Tilt);
        var turn = Matrix4x4.CreateRotationZ(-heading);
        return Matrix4x4.CreateScale(scale) * tilt * turn * Matrix4x4.CreateTranslation(position + _state.Offsets[index]);
    }
}
