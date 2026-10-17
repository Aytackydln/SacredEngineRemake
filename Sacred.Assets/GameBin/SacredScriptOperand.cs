using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>A completely bounded operand; symbolic values are resolved by the execution context.</summary>
public readonly record struct SacredScriptOperand(SacredScriptArgumentKind Kind, int Offset,
    string? Text = null, int? Integer = null, SacredScriptPosition? Position = null);
