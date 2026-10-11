namespace Sacred.Particles;

public sealed record SacredHolyShieldFxParameters(float RotationRate, float RotationWrap, int HitBrightness,
    float HitFadeRate, float GradientIndexScale, int GradientStart, int GradientStep,
    float GroundHalfSize, float GroundAngleScale, float GroundAngleOrigin, float GroundAngleBias, bool GroundSourceColorOnly = true);
