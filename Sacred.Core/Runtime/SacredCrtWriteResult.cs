namespace Sacred.Core.Runtime;

public readonly record struct SacredCrtWriteResult(bool Success, int Count, uint Error = 0);
