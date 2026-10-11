using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native float stores, strict boundaries, layer carry and flag-consuming toggle.</summary>
public sealed class SacredControlledFlareFxPlayback
{
    private SacredRevivalFlareStateLayout _revival;
    private SacredDeathCallFlareStateLayout _death;
    private SacredToggleTrapFlareStateLayout _trap;
    private ushort _flags;
    public SacredControlledFlareFxPlayback(SacredControlledFlareFxDefinition definition)
    { Definition = definition; Restart(); }
    public SacredControlledFlareFxDefinition Definition { get; }
    public uint Notification { get; private set; }
    public bool Active => Definition.Kind switch
    { SacredControlledFlareKind.Revival => _revival.Active != 0,
        SacredControlledFlareKind.DeathCall => _death.Active != 0, _ => _trap.Active != 0 };
    public SacredControlledFlareSnapshot CaptureState() => new(_revival, _death, _trap, _flags, Notification);
    public void RestoreState(SacredControlledFlareSnapshot state)
    { _revival = state.Revival; _death = state.DeathCall; _trap = state.Trap; _flags = state.Flags; Notification = state.Notification; }
    public void Restart()
    {
        _revival = new() { Active = 1, Rising = 1 };
        _death = new() { Active = 1, LayerWeight = Definition.InitialWeight, Radius = Definition.InitialRadius };
        _trap = new() { Active = 1, AtlasCell = Definition.InitialAtlasCell };
        _flags = 0; Notification = Definition.InitialNotification;
    }
    public void RequestToggle()
    {
        if (Definition.Kind != SacredControlledFlareKind.ToggleTrap) throw new InvalidOperationException("This flare has no native toggle flag.");
        _flags |= Definition.ToggleFlag;
    }
    public void SetAtlasCell(int cell)
    {
        if (Definition.Kind != SacredControlledFlareKind.ToggleTrap || (uint)cell >= Definition.AtlasSide * Definition.AtlasSide)
            throw new ArgumentOutOfRangeException(nameof(cell));
        _trap.AtlasCell = cell;
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        switch (Definition.Kind)
        {
            case SacredControlledFlareKind.Revival:
                _revival.ElapsedTime = (float)(_revival.ElapsedTime + (double)seconds);
                var angle = _revival.Angle + seconds * (double)Definition.RotationRate;
                _revival.Angle = (float)angle;
                if (angle > Definition.FullAngle) _revival.Angle = (float)(angle - Definition.FullAngle);
                var opacity = _revival.Opacity + seconds * (double)(_revival.Rising != 0 ? Definition.RiseRate : -Definition.FallRate);
                _revival.Opacity = (float)opacity;
                if (_revival.Rising != 0 && opacity > 1) { _revival.Opacity = 1; _revival.Rising = 0; }
                else if (_revival.Rising == 0 && opacity < 0) { _revival.Opacity = 0; _revival.Active = 0; }
                break;
            case SacredControlledFlareKind.DeathCall:
                _death.ElapsedTime = (float)(_death.ElapsedTime + (double)seconds);
                var weight = _death.LayerWeight - seconds * (double)Definition.FallRate;
                _death.LayerWeight = (float)weight;
                if (weight <= 0) { _death.LayerWeight = 0; _death.Active = 0; }
                break;
            case SacredControlledFlareKind.ToggleTrap:
                _trap.ElapsedTime = (float)(_trap.ElapsedTime + (double)seconds);
                if (_trap.Brightness > 0) _trap.Brightness = Math.Max(0, (float)(_trap.Brightness - seconds * (double)Definition.FallRate));
                if ((_flags & Definition.ToggleFlag) != 0)
                {
                    _flags &= (ushort)~Definition.ToggleFlag; _trap.Enabled = (byte)(_trap.Enabled == 0 ? 1 : 0);
                    _trap.Brightness = 1; Notification = _trap.Enabled != 0 ? Definition.ToggleNotification : 0;
                }
                break;
        }
    }
    /// <summary>Includes native invisible final passes. The object manager separately checks Active.</summary>
    public IReadOnlyList<SacredControlledFlareSurface> Surfaces()
    {
        var result = new List<SacredControlledFlareSurface>();
        if (Definition.Kind == SacredControlledFlareKind.Revival)
        {
            var intensity = unchecked((uint)(int)(_revival.Opacity * (double)Definition.AlphaScale));
            for (var index = 0; index < Definition.Layers.Count; index++)
            {
                var layer = Definition.Layers[index];
                var color = layer.SourceColorOnly ? intensity << 16 | intensity << 8 | intensity : intensity << 24 | layer.Color;
                result.Add(new(layer with { Color = color }, (float)(_revival.Angle * (double)layer.RotationRate)));
            }
        }
        else if (Definition.Kind == SacredControlledFlareKind.DeathCall && _death.ElapsedTime != 0)
        {
            var time = _death.ElapsedTime; var whole = (int)_death.LayerWeight;
            var size = (float)(_death.Radius * (double)Definition.RadiusScale);
            for (var index = 0; index <= whole; index++)
            {
                var alpha = index == whole ? unchecked((uint)(int)((_death.LayerWeight - Math.Floor(_death.LayerWeight)) * Definition.AlphaScale)) : 255u;
                result.Add(new(Definition.Layers[0] with { HalfSize = size, Color = alpha << 24 | Definition.Layers[0].Color },
                    (float)(Definition.RotationNumerator / (double)time)));
                time = (float)(time + (double)Definition.LayerTimeIncrement);
            }
        }
        else if (Definition.Kind == SacredControlledFlareKind.ToggleTrap)
        {
            var intensity = _trap.Brightness > 0 ? unchecked((uint)(int)(_trap.Brightness * (double)Definition.AlphaScale)) : 0;
            var color = intensity << 16 | 0xFF00 | intensity;
            // Red when enabled, green when disabled; the other channels carry the flash.
            if (_trap.Enabled != 0) color = 0xFF0000 | intensity << 8 | intensity;
            result.Add(new(Definition.Layers[0] with { Color = color,
                HalfSize = (float)(Definition.Layers[0].HalfSize + _trap.Brightness * (double)Definition.RadiusAmplitude) },
                (float)(_trap.ElapsedTime * (double)Definition.RotationRate), _trap.AtlasCell, Definition.AtlasSide));
        }
        return result;
    }
}
