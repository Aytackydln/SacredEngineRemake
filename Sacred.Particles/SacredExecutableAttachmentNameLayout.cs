using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles;

/// <summary>Gold 0x401000 indexes this 32-byte GRN helper name record in
/// the native table at 0x8E7200. This index is not a model skeleton index.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredExecutableAttachmentNameLayout
{
    public const int SerializedSize = 32;
    [FieldOffset(0), BinaryString("Name", SerializedSize, "ISO-8859-1")]
    private readonly byte _name;
}
