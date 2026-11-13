namespace Sacred.Assets.GameBin;

/// <summary>A displayed operand and its exact encoded bytes, including its tag.
/// Offset is relative to the instruction header. A raw suffix retains everything
/// after the first unknown, context-dependent or truncated operand.</summary>
public sealed record SacredScriptDisplayOperand(int Offset, ReadOnlyMemory<byte> Bytes, string Text, bool IsRaw);
