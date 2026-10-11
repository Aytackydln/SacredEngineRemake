namespace Sacred.Particles;

/// <summary>Shared five-model spike burst used by both Anducar effect rows.</summary>
public sealed record SacredAnducarFxDefinition(IReadOnlyList<string> ModelNames, string SurfaceTexture, string GlowTexture)
{
    public byte Count { get; init; }
    public bool HasGlowPass { get; init; }
    public float RandomScale { get; init; }
    public float ScaleOrigin { get; init; }
    public float ScaleWidth { get; init; }
    public float FullTurn { get; init; }
    public float OffsetWidth { get; init; }
    public float OffsetOrigin { get; init; }
    public float DelayPerUnit { get; init; }
    public float HoldSeconds { get; init; }
    public float EnvelopeRate { get; init; }
    public float HorizontalScaleFactor { get; init; }
    public uint GlowColor { get; init; }
    public bool GlowSourceColorOnly { get; init; }
    /// <summary>Native object +7C notification when any model's delay expires.</summary>
    public uint StartNotification { get; init; }
}
