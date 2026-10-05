namespace Sacred.Particles;

/// <summary>Native billboard half-size range; zero frequency selects per-draw random variation.</summary>
public sealed record SacredBillboardSizeAnimation(float MinimumHalfSize, float MaximumHalfSize,
    float AngularFrequency = 0);
