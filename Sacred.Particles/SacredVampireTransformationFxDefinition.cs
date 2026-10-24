namespace Sacred.Particles;

/// <summary>Native vamp_sm_cast's animated source, three particle palettes and two tip flares.</summary>
public sealed record SacredVampireTransformationFxDefinition(string Animation, string SourceBone,
    string GlowTexture, float GlowHalfSize, uint GlowColor, string SparkTexture, float SparkHalfSize,
    uint SparkColor);
