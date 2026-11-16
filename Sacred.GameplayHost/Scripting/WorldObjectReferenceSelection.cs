namespace Sacred.GameplayHost.Scripting;

/// <summary>A null allocation byte means the caller supplied no output pointer;
/// otherwise unchanged bytes remain unchanged unless an old marked object is reused.</summary>
public readonly record struct WorldObjectReferenceSelection(uint Reference, byte? AllocationByte);

/// <summary>Native pending pair appended after a nonzero selection, before cleanup.</summary>
public readonly record struct WorldObjectReferenceDelay(uint Reference, uint Time);
