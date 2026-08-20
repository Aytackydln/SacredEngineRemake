using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Models;

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
    public const int SerializedSize = 0x47C;

    /// <summary>Null-terminated model name from the payload prefix.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ModelName", 0x40, "ISO-8859-1")]
    private readonly byte _modelName;

    /// <summary>Default motion-table index used when no state-specific motion is selected.</summary>
    [FieldOffset(0x74)]
    public readonly uint DefaultMotionIndex;

    /// <summary>Model-space scale on the X axis.</summary>
    [FieldOffset(0x470)]
    public readonly float ScaleX;

    /// <summary>Model-space scale on the Y axis.</summary>
    [FieldOffset(0x474)]
    public readonly float ScaleY;

    /// <summary>Model-space scale on the Z axis.</summary>
    [FieldOffset(0x478)]
    public readonly float ScaleZ;
}
