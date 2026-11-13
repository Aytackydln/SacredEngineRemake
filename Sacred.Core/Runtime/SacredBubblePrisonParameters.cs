namespace Sacred.Core.Runtime;

/// <summary>Executable operands used by BUBBLEPRISON Start (Gold 0x79C860).</summary>
public sealed record SacredBubblePrisonParameters(float RandomScale, float FullTurn,
    float AngularVelocityScale, float AngularVelocityBias, float HeightStep,
    float LastAngularVelocityScale, float LastAngularVelocityBias, float LastHeight);
