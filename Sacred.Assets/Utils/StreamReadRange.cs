namespace Sacred.Assets.Utils;

/// <summary>A byte range at an absolute stream offset.</summary>
public readonly record struct StreamReadRange(long Offset, int Length);
