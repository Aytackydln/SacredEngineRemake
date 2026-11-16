namespace Sacred.Core.Runtime;

/// <summary>One original ordered text pass. RemovalLength is both the minimum
/// whole-string length before searching and the maximum code units erased.</summary>
public readonly record struct SacredTextMarkupRemoval(string Prefix, int RemovalLength);
