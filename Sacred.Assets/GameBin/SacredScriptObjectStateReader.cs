using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Literal SetObjState name and ordered Open/Close/Lock/Unlock operands.
/// The native pending state keeps open and lock requests independent.</summary>
public static class SacredScriptObjectStateReader
{
    public static SacredScriptObjectStateChange Read(SacredScriptCommand command)
    {
        var bytes=command.Bytes.Span;
        if (bytes.Length<4 || command.Opcode!=SacredScriptDoorOpcodes.SetObjectState || BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..])!=bytes.Length)
            throw new InvalidDataException("SetObjState requires a complete opcode-2 instruction.");
        string? name=null;short open=-1,locked=-1;
        for (var at=4;at<bytes.Length;)
        {
            var tag=bytes[at++];
            if (tag==(byte)SacredScriptArgumentKind.NullTerminatedString)
            {
                var end=bytes[at..].IndexOf((byte)0);
                if (end<0) throw new InvalidDataException("SetObjState name is unterminated.");
                name=Encoding.Latin1.GetString(bytes.Slice(at,end));at+=end+1;
            }
            else switch ((SacredScriptObjectStateOperand)tag)
            {
                case SacredScriptObjectStateOperand.Lock: locked=1;break;
                case SacredScriptObjectStateOperand.Unlock: locked=0;break;
                case SacredScriptObjectStateOperand.Open: open=1;break;
                case SacredScriptObjectStateOperand.Close: open=0;break;
                default: throw new NotSupportedException($"SetObjState requires its native operand 0x{tag:X2} at +0x{at-1:X}.");
            }
        }
        return new(name,open,locked);
    }
}
