namespace Sacred.Core.Executable.Layouts;

/// <summary>Gold 0x803EA0 operand purposes, relative to the compared function.
/// Referenced UTF16 strings and erase/gate lengths are executable data, not defaults.</summary>
public static class SacredExecutableRosterIdentifierLayout
{
    public const int BodySize = 0x5cf;
    /// <summary>The structural prologue starts after the relocated exception
    /// handler operand and FS load. That address does not define text behavior.</summary>
    public const int PrologueOffset = 13;
    public static ReadOnlySpan<byte> Prologue =>
        [0x50, 0x64, 0x89, 0x25, 0, 0, 0, 0, 0x83, 0xec, 0x1c, 0x53, 0x56, 0xc7, 0x44, 0x24, 0x0c, 0, 0, 0, 0];

    /// <summary>Operands of the seven push-prefix instructions, in pass order.</summary>
    public static ReadOnlySpan<int> PrefixOperandOffsets => [0x73, 0x11c, 0x1c5, 0x26e, 0x317, 0x3c0, 0x46e];

    /// <summary>The first four passes share EDI's immediate, the next two share
    /// EBX's immediate, and the last reloads EDI. Each value gates and sizes erasure.</summary>
    public static ReadOnlySpan<int> RemovalLengthOperandOffsets => [0x60, 0x60, 0x60, 0x60, 0x59, 0x59, 0x45d];

    /// <summary>Each prefix is pushed again for ordinal find, twenty bytes later.</summary>
    public const int RepeatedPrefixOperandDistance = 0x14;
}
