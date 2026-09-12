using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Models;

[InlineArray(16)]
public struct ModelChunkParameters
{
    private int _element0;
}

[InlineArray(256)]
public struct ModelChunkMotionIndexes
{
    private uint _element0;
}

[InlineArray(3)]
public struct ModelChunkScale
{
    private float _element0;
}

[InlineArray(11)]
public struct ModelChunkReservedWords
{
    private uint _element0;
}

/// <summary>Descriptor for one model payload in models.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelPakDescriptorLayout
{
    /// <summary>Serialized size of one descriptor.</summary>
    public const int SerializedSize = 0x0C;

    /// <summary>Model entry identifier.</summary>
    [FieldOffset(0x00)]
    public readonly uint EntryId;

    /// <summary>Absolute byte offset of the model payload.</summary>
    [FieldOffset(0x04)]
    public readonly uint Offset;

    /// <summary>Authored payload-size value from the descriptor.</summary>
    [FieldOffset(0x08)]
    public readonly uint PayloadSize;
}

/// <summary>
/// Known metadata fields in the prefix of a models.pak payload. The payload can
/// continue with variable-size Granny model data beyond this prefix.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelPakPayloadMetadataLayout
{
    /// <summary>Minimum payload length needed to contain every mapped metadata field.</summary>
    public const int SerializedSize = 0x4AA;

    /// <summary>Null-terminated model name from the payload prefix.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ModelName", 0x40, "ISO-8859-1")]
    private readonly byte _modelName;

    /// <summary>Native <c>cGrannyModelChunk::params[16]</c>.</summary>
    [FieldOffset(0x30)]
    public readonly ModelChunkParameters Parameters;

    /// <summary>Native <c>cGrannyModelChunk::motions[256]</c>.</summary>
    [FieldOffset(0x70)]
    public readonly ModelChunkMotionIndexes MotionIndexes;

    /// <summary>Second motion-table index, used by the existing default-animation loader.</summary>
    [FieldOffset(0x74)]
    public readonly uint DefaultMotionIndex;

    /// <summary>Native <c>cGrannyModelChunk::scale[3]</c>.</summary>
    [FieldOffset(0x470)]
    public readonly ModelChunkScale Scale;

    /// <summary>Model-space scale on the X axis.</summary>
    [FieldOffset(0x470)]
    public readonly float ScaleX;

    /// <summary>Model-space scale on the Y axis.</summary>
    [FieldOffset(0x474)]
    public readonly float ScaleY;

    /// <summary>Model-space scale on the Z axis.</summary>
    [FieldOffset(0x478)]
    public readonly float ScaleZ;

    /// <summary>Native <c>cGrannyModelChunk::reserved[11]</c>.</summary>
    [FieldOffset(0x47C)]
    [BinaryUnknown]
    public readonly ModelChunkReservedWords Reserved;

    /// <summary>Native <c>cGrannyModelChunk::fileEntry</c>.</summary>
    [FieldOffset(0x4A8)]
    public readonly ushort FileEntry;
}
