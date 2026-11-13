namespace Sacred.Core.GameBin.Scripts;

/// <summary>Operand wire shapes used for inspection, adapted from the Armalion editor's
/// script_text.rs. Unknown or context-dependent shapes must retain their original bytes.
/// These shapes do not authorize execution. Gold's trigger scope is one byte, not UInt16.</summary>
public static class SacredScriptDisplayOperandLayout
{
    public static SacredScriptDisplayOperandShape GetShape(byte tag) => tag switch
    {
        0x01 or 0x05 or 0x09 or 0x16 or 0x1E or 0x29 or 0x41 or 0x60 or
        0x63 or 0x67 or 0x69 or 0x83 or 0x40 or 0x52 => SacredScriptDisplayOperandShape.String,
        0x02 or 0x0B or 0x11 or 0x1C or 0x1D or 0x1F or 0x38 or 0x53 or
        0x75 or 0x7E or 0x90 or 0x36 => SacredScriptDisplayOperandShape.Integer,
        0x03 or 0x0A or 0x6B => SacredScriptDisplayOperandShape.Word,
        0x28 or 0x8B => SacredScriptDisplayOperandShape.Byte,
        0x4D or 0x79 or 0x33 or 0x34 or 0x87 or 0x88 or 0x89 => SacredScriptDisplayOperandShape.Pair,
        0x20 or 0x35 or 0x19 => SacredScriptDisplayOperandShape.Triple,
        0x04 or 0x0C or 0x0D or 0x2A or 0x3C => SacredScriptDisplayOperandShape.Position,
        0x48 or 0x49 or 0x4A or 0x6D or 0x6E => SacredScriptDisplayOperandShape.IntegerString,
        0x06 or 0x07 or 0x08 or 0x0E or 0x0F or 0x10 or 0x12 or 0x13 or 0x14 or
        0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x2B or 0x2C or 0x2D or 0x37 or 0x39 or
        0x42 or 0x46 or 0x4B or 0x4C or 0x4E or 0x4F or 0x61 or 0x62 or 0x64 or 0x65 or
        0x66 or 0x71 or 0x72 or 0x74 or 0x7C or 0xA0 => SacredScriptDisplayOperandShape.Flag,
        _ => SacredScriptDisplayOperandShape.Unknown
    };
}

public enum SacredScriptDisplayOperandShape
{
    Unknown, Flag, Byte, Word, Integer, Pair, Triple, Position, String, IntegerString
}
