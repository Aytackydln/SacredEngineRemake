namespace Sacred.Core.Executable;

/// <summary>Original six-byte prefixes and CRT classes indexed by signed char.</summary>
public sealed record SacredRegionFunctionBindingDefinition(ReadOnlyMemory<byte> FirstPrefix,
    ReadOnlyMemory<byte> SecondPrefix, ReadOnlyMemory<ushort> SignedClasses);
