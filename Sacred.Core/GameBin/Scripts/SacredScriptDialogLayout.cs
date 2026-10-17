using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>DefPos.bin's second counted table and Gold world bank 4 section 0.
/// Gold 0x46FE71 reads positions (100 bytes), then these 80-byte dialog records.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptDialogLayout
{
    public const int SerializedSize = 80;
    public const int NameOffset = 4;
    public const int NameLength = 64;
    public const int FunctionOffset = 68;
    public const int IconOffset = 72;
    public const int AttachedObjectReferenceOffset = 76;
    /// <summary>Native first word; its role is unresolved. It is not the independent network update slot.</summary>
    [FieldOffset(0), BinaryUnknown] public readonly int Number;
    [FieldOffset(NameOffset), BinaryString("Name", NameLength, "ISO-8859-1")] private readonly byte _name;
    /// <summary>Function index in the file; native network import resolves the function reference.</summary>
    [FieldOffset(FunctionOffset)] public readonly uint Function;
    /// <summary>Native overhead selector read by 0x499E90; SetIcon (opcode 86) changes this word.</summary>
    [FieldOffset(IconOffset)] public readonly uint Icon;
    /// <summary>Gold CreateNPC 0x483AD5 stores creature +0x0C here when binding this dialog;
    /// 0x483AA0 clears the previous dialog attachment. Also present in bank 4 and 0x1C8.</summary>
    [FieldOffset(AttachedObjectReferenceOffset)] public readonly uint AttachedObjectReference;
}
