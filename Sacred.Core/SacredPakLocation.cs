namespace Sacred.Core;

public readonly record struct SacredPakLocation(
    SacredPakFile PakFile,
    long Offset,
    int Length
);