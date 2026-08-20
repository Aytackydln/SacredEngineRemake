using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Models;

/// <summary>Thirteen consecutive 32-bit motion-table indexes.</summary>
[InlineArray(13)]
public struct ModelMotionIndexArray13
{
    private uint _element0;
}

/// <summary>Header of the Models.tmp companion metadata table.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelsMetadataHeaderLayout
{
    /// <summary>Serialized header size before model records.</summary>
    public const int SerializedSize = 0x118;

    /// <summary>Number of 1,194-byte model metadata records.</summary>
    [FieldOffset(0x10)] public readonly uint ModelCount;

    /// <summary>Number of 256-byte motion-name records following the model table.</summary>
    [FieldOffset(0x14)] public readonly uint MotionCount;
}

/// <summary>Known fields in one Models.tmp model metadata record.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelsMetadataModelLayout
{
    /// <summary>Serialized size of one model metadata record.</summary>
    public const int SerializedSize = 1194;

    /// <summary>Null-terminated model name encoded as ISO-8859-1.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ModelName", 0x20, "ISO-8859-1")]
    private readonly byte _modelName;

    /// <summary>Idle motion indexes for the first thirteen weapon styles.</summary>
    [FieldOffset(116)] public readonly ModelMotionIndexArray13 IdleMotionIndexes;
    /// <summary>Fighting-idle motion indexes for the first thirteen weapon styles.</summary>
    [FieldOffset(168)] public readonly ModelMotionIndexArray13 FightingIdleMotionIndexes;
    /// <summary>Walk motion indexes for the first thirteen weapon styles.</summary>
    [FieldOffset(220)] public readonly ModelMotionIndexArray13 WalkMotionIndexes;
    /// <summary>Run motion indexes for the first thirteen weapon styles.</summary>
    [FieldOffset(272)] public readonly ModelMotionIndexArray13 RunMotionIndexes;
    /// <summary>Defend motion indexes for the first thirteen weapon styles.</summary>
    [FieldOffset(324)] public readonly ModelMotionIndexArray13 DefendMotionIndexes;

    /// <summary>Attack motion index for weapon style 0.</summary>
    [FieldOffset(428)] public readonly uint AttackMotionIndex0;
    /// <summary>Attack motion index for weapon style 1.</summary>
    [FieldOffset(448)] public readonly uint AttackMotionIndex1;
    /// <summary>Attack motion index for weapon style 2.</summary>
    [FieldOffset(468)] public readonly uint AttackMotionIndex2;
    /// <summary>Attack motion index for weapon style 3.</summary>
    [FieldOffset(488)] public readonly uint AttackMotionIndex3;
    /// <summary>Attack motion index for weapon style 4.</summary>
    [FieldOffset(508)] public readonly uint AttackMotionIndex4;
    /// <summary>Attack motion index for weapon style 5.</summary>
    [FieldOffset(528)] public readonly uint AttackMotionIndex5;
    /// <summary>Attack motion index for weapon style 6.</summary>
    [FieldOffset(548)] public readonly uint AttackMotionIndex6;
    /// <summary>Attack motion index for weapon style 7.</summary>
    [FieldOffset(568)] public readonly uint AttackMotionIndex7;
    /// <summary>Attack motion index for weapon style 8.</summary>
    [FieldOffset(588)] public readonly uint AttackMotionIndex8;
    /// <summary>Attack motion index for weapon style 9.</summary>
    [FieldOffset(608)] public readonly uint AttackMotionIndex9;
    /// <summary>Attack motion index for weapon style 10.</summary>
    [FieldOffset(628)] public readonly uint AttackMotionIndex10;
    /// <summary>Attack motion index for weapon style 11.</summary>
    [FieldOffset(648)] public readonly uint AttackMotionIndex11;
    /// <summary>Attack motion index for weapon style 12.</summary>
    [FieldOffset(668)] public readonly uint AttackMotionIndex12;

    /// <summary>Idle motion index for the fourteenth weapon style.</summary>
    [FieldOffset(1052)] public readonly uint IdleMotionIndex13;
    /// <summary>Fighting-idle motion index for the fourteenth weapon style.</summary>
    [FieldOffset(1056)] public readonly uint FightingIdleMotionIndex13;
    /// <summary>Walk motion index for the fourteenth weapon style.</summary>
    [FieldOffset(1060)] public readonly uint WalkMotionIndex13;
    /// <summary>Run motion index for the fourteenth weapon style.</summary>
    [FieldOffset(1064)] public readonly uint RunMotionIndex13;
    /// <summary>Attack motion index for the fourteenth weapon style.</summary>
    [FieldOffset(1076)] public readonly uint AttackMotionIndex13;
}

/// <summary>One fixed-size motion-name record in Models.tmp.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct ModelsMetadataMotionLayout
{
    /// <summary>Serialized size of one motion-name record.</summary>
    public const int SerializedSize = 256;

    /// <summary>Null-terminated motion name encoded as ISO-8859-1.</summary>
    [FieldOffset(0x00)]
    [BinaryString("MotionName", 0x20, "ISO-8859-1")]
    private readonly byte _motionName;
}
