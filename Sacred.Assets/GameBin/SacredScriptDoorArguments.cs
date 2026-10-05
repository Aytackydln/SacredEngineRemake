using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Decodes trigger declarations, patch cells and literal object-state commands.</summary>
public sealed class SacredScriptDoorArguments
{
    public string? Name { get; private set; }
    public bool? Open { get; private set; }
    public bool? Locked { get; private set; }
    public byte? TriggerType { get; private set; }
    public List<SacredScriptPosition> Cells { get; } = [];

    public static SacredScriptDoorArguments? Read(SacredScriptCommand command)
    {
        var data = command.Bytes.Span;
        if (data.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            return null;
        var result = new SacredScriptDoorArguments();
        var offset = 4;
        while (offset < data.Length)
        {
            var tag = data[offset++];
            switch (tag)
            {
                case (byte)SacredScriptArgumentKind.NullTerminatedString:
                    var end = data[offset..].IndexOf((byte)0);
                    if (end < 0) return null;
                    result.Name = Encoding.Latin1.GetString(data.Slice(offset, end));
                    offset += end + 1;
                    break;
                case SacredScriptTriggerTypeLayout.OperandKind:
                    if (offset >= data.Length) return null;
                    result.TriggerType = data[offset++];
                    break;
                case (byte)SacredScriptArgumentKind.TriggerPatchPosition:
                    if (data.Length - offset < 12) return null;
                    result.Cells.Add(new(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]),
                        BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]),
                        BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 8)..])));
                    offset += SacredScriptTriggerPatchPositionLayout.SerializedSize - 1;
                    break;
                case (byte)SacredScriptObjectStateOperand.Open:
                case (byte)SacredScriptObjectStateOperand.TriggerOpen:
                    result.Open = true;
                    break;
                case (byte)SacredScriptObjectStateOperand.Close:
                case (byte)SacredScriptObjectStateOperand.TriggerClose:
                    result.Open = false;
                    break;
                case (byte)SacredScriptObjectStateOperand.Lock:
                case (byte)SacredScriptObjectStateOperand.TriggerLock:
                    result.Locked = true;
                    result.Open = false;
                    break;
                case (byte)SacredScriptObjectStateOperand.Unlock:
                case (byte)SacredScriptObjectStateOperand.TriggerUnlock:
                    result.Locked = false;
                    break;
                default: return null;
            }
        }
        return result.Name is null ? null : result;
    }
}
