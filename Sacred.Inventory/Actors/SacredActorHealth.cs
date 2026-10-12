using System;
using Sacred.Core.Runtime;

namespace Sacred.Inventory.Actors;

/// <summary>Current health owned by the native character component, separate from calculated stats.</summary>
public sealed class SacredActorHealth
{
    public int? MaximumHitPoints { get; private set; }
    public int? CurrentHitPoints { get; private set; }
    /// <summary>New actors start fully healed; absolute HP can be supplied by calculation or native import.</summary>
    public float Fraction { get; private set; } = 1;
    public event Action? Changed;

    public void SetHitPoints(int current, int maximum)
    {
        if (maximum <= 0 || current < 0 || current > maximum) throw new ArgumentOutOfRangeException(nameof(current));
        MaximumHitPoints = maximum; CurrentHitPoints = current; Fraction = current / (float)maximum;
        Changed?.Invoke();
    }

    public void ImportNativeComponent(in SacredActorComponentLayout component) =>
        SetHitPoints(component.CurrentHitPoints, component.MaximumHitPoints);

    public void SetFraction(float fraction)
    {
        if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1) throw new ArgumentOutOfRangeException(nameof(fraction));
        Fraction = fraction;
        if (MaximumHitPoints is { } maximum)
        { CurrentHitPoints = (int)(maximum * fraction); Fraction = CurrentHitPoints.Value / (float)maximum; }
        Changed?.Invoke();
    }

    public void RestoreFullHealth() => SetFraction(1);

    internal void CopyTo(SacredActorHealth other)
    { other.MaximumHitPoints = MaximumHitPoints; other.CurrentHitPoints = CurrentHitPoints; other.Fraction = Fraction; }
}
