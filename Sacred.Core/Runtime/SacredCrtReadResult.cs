namespace Sacred.Core.Runtime;

/// <summary>Supplied ReadFile outcome; Count is delivered bytes on success.
/// Win32 error codes are retained at this boundary, not inferred from errno.</summary>
public readonly record struct SacredCrtReadResult(bool Success, int Count, uint Error = 0);
