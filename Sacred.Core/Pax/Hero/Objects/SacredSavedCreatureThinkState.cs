namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Original scheduler inputs retained by the saved creature stream, but omitted from Gold packets.</summary>
public readonly record struct SacredSavedCreatureThinkState(float Interval, float Elapsed, ushort KiMode, ushort EiMode);
