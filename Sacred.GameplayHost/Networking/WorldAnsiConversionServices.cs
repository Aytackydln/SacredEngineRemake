namespace Sacred.GameplayHost.Networking;

/// <summary>Windows API and temporary-allocation boundaries. Decode calls use
/// codepage0/flags0/source length-1; Encode uses codepage0/flags0/explicit UTF-16
/// length and null default-character pointers. Empty destination means size query.
/// Allocators expose actual initialized contents; no zeroed-heap assumption is made.</summary>
public sealed record WorldAnsiConversionServices(
    Func<ReadOnlyMemory<byte>, Memory<char>, int> Decode,
    Func<ReadOnlyMemory<char>, Memory<byte>, int> Encode,
    Func<int, Memory<char>> AllocateWide, Func<int, Memory<byte>> AllocateAnsi);
