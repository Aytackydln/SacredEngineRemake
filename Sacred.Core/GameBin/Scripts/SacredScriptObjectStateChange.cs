namespace Sacred.Core.GameBin.Scripts;

/// <summary>Independent native object open/lock requests; -1 leaves a state unchanged.</summary>
public readonly record struct SacredScriptObjectStateChange(string? Name,short Open,short Locked);
