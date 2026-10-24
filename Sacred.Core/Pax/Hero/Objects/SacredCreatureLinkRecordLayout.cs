using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Native creature +39C vector element, shared by saved creatures and Gold creature tails.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredCreatureLinkRecordLayout
{
    public const int SerializedSize = 9;
    public const int ReferenceOffset = 0;
    public const int RetainedWordOffset = 4;
    public const int OptionOffset = 8;
    [FieldOffset(ReferenceOffset)] public readonly uint ObjectReference;
    /// <summary>Zero for a new ordinary link; equipment links can store 100. Broader meaning is unresolved.</summary>
    [FieldOffset(RetainedWordOffset), BinaryUnknown] public readonly uint RetainedWord;
    /// <summary>12F's truthy option for a player parent; non-player parents append zero instead.</summary>
    [FieldOffset(OptionOffset)] public readonly byte Option;
}
