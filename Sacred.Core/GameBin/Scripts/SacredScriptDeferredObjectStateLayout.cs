using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x48B99B pending SetObjState entry at interpreter +0x7520.
/// This private vector is separate from bank-2 callback rows at +0x358. Interpreter
/// save 0x46715B writes count then rows; CreateObj 0x486BAB consumes them by
/// swapping the last row into the matching slot. Name tails retain scratch.</summary>
[StructLayout(LayoutKind.Explicit, Pack=1, Size=SerializedSize)]
public readonly struct SacredScriptDeferredObjectStateLayout
{
    public const int SerializedSize=68;
    public const int InterpreterOffset=0x7520;
    public const int SaveCountSize=4;
    public const int NameLength=64;
    public const int StateOffset=64;
    [FieldOffset(0),BinaryString("Name",NameLength,"ISO-8859-1")] private readonly byte _name;
    /// <summary>FFFF unchanged, normally 0 close / 1 open. CreateObj zero-extends
    /// every other UInt16 payload to its Int32 local.</summary>
    [FieldOffset(StateOffset)] public readonly short Open;
    /// <summary>FFFF unchanged, normally 0 unlock / 1 lock. Other UInt16
    /// payloads are zero-extended into CreateObj's Int32 local.</summary>
    [FieldOffset(StateOffset+2)] public readonly short Locked;
}

