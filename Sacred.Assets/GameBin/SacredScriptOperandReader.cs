using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Complete operands for definitions, variables, calls and base triggers.
/// Unsupported representations fail at their original operand offset.</summary>
public static class SacredScriptOperandReader
{
    public static IReadOnlyList<SacredScriptOperand> Read(SacredScriptCommand command)
    {
        var data = command.Bytes.Span;
        if (data.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            throw new InvalidDataException("Invalid script instruction header.");
        var result = new List<SacredScriptOperand>();
        var offset = 4;
        while (offset < data.Length)
        {
            var start = offset;
            var kind = (SacredScriptArgumentKind)data[offset++];
            switch (kind)
            {
                case SacredScriptArgumentKind.NullTerminatedString:
                case SacredScriptArgumentKind.ObjectReference:
                case SacredScriptArgumentKind.ObjectTakeScript:
                case SacredScriptArgumentKind.ObjectUseScript:
                case SacredScriptArgumentKind.CreatureSpellSelector:
                    result.Add(new(kind, start, Text: ReadString(data, ref offset)));
                    break;
                case SacredScriptArgumentKind.TypeId:
                case SacredScriptArgumentKind.CreatureGroup:
                case SacredScriptArgumentKind.ScriptResourceId:
                case SacredScriptArgumentKind.PopulationCreatureValue:
                    result.Add(new(kind, start, Integer: ReadInt(data, ref offset)));
                    break;
                case SacredScriptArgumentKind.CreatureLevel:
                    if (data.Length - offset >= SacredScriptVariableExpressionLayout.PrefixSize &&
                        data[offset] == SacredScriptVariableExpressionLayout.Marker &&
                        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 1)..]) == SacredScriptVariableExpressionLayout.Signature)
                    {
                        offset += SacredScriptVariableExpressionLayout.PrefixSize;
                        result.Add(new(kind, start, Text: ReadString(data, ref offset)));
                    }
                    else result.Add(new(kind, start, Integer: ReadInt(data, ref offset)));
                    break;
                case SacredScriptArgumentKind.Facing:
                case SacredScriptArgumentKind.CreatureFlag2B7Mask08:
                    if (data.Length - offset < 2) throw new InvalidDataException($"Truncated UInt16 operand at +0x{start:X}.");
                    result.Add(new(kind, start, Integer: BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])));
                    offset += 2;
                    break;
                case SacredScriptArgumentKind.HeightOffset:
                    if (data.Length - offset < 4) throw new InvalidDataException($"Truncated height at +0x{start:X}.");
                    result.Add(new(kind, start, Integer: BinaryPrimitives.ReadInt16LittleEndian(data[offset..])));
                    offset += 4; // Native consumes four bytes but reads only the signed low word.
                    break;
                case SacredScriptArgumentKind.Integer:
                    var integer = ReadInt(data, ref offset);
                    result.Add(integer == SacredScriptVariableArgumentLayout.VariableSentinel
                        ? new(kind, start, Text: ReadString(data, ref offset)) : new(kind, start, Integer: integer));
                    break;
                case SacredScriptArgumentKind.TriggerScope:
                case SacredScriptArgumentKind.SpawnEquipmentSelector:
                    if (offset == data.Length) throw new InvalidDataException($"Truncated trigger scope at +0x{start:X}.");
                    result.Add(new(kind, start, Integer: data[offset++]));
                    break;
                case SacredScriptArgumentKind.SpawnEquipmentFirstPool:
                case SacredScriptArgumentKind.SpawnEquipmentSecondPool:
                case SacredScriptArgumentKind.SpawnEquipmentThirdPool:
                case SacredScriptArgumentKind.PopulationLevels:
                case SacredScriptArgumentKind.PopulationSector:
                    result.Add(new(kind, start, Integer: ReadInt(data, ref offset), SecondInteger: ReadInt(data, ref offset)));
                    break;
                case SacredScriptArgumentKind.PopulationDensity:
                case SacredScriptArgumentKind.PopulationWeightedType:
                    result.Add(new(kind, start, Integer: ReadInt(data, ref offset), SecondInteger: ReadInt(data, ref offset),
                        ThirdInteger: ReadInt(data, ref offset)));
                    break;
                case SacredScriptArgumentKind.TriggerStateOn:
                case SacredScriptArgumentKind.TriggerStateOff:
                case SacredScriptArgumentKind.TriggerState8000On:
                case SacredScriptArgumentKind.TriggerState8000Off:
                case SacredScriptArgumentKind.CreatureNonAggressive:
                case SacredScriptArgumentKind.CreatureAggressive:
                case SacredScriptArgumentKind.CreatureBehavior1:
                case SacredScriptArgumentKind.CreatureBehavior2:
                case SacredScriptArgumentKind.CreatureBehavior0:
                case SacredScriptArgumentKind.CreatureBehavior4000:
                case SacredScriptArgumentKind.CreatureBehavior10:
                case SacredScriptArgumentKind.CreatureBehavior40:
                case SacredScriptArgumentKind.CreatureBehavior40Alias:
                case SacredScriptArgumentKind.CreatureBehavior8:
                case SacredScriptArgumentKind.CreatureBehavior20:
                case SacredScriptArgumentKind.CreatureBehavior80:
                case SacredScriptArgumentKind.CreatureBehavior400:
                case SacredScriptArgumentKind.CreatureTrader:
                case SacredScriptArgumentKind.CreatureComboMaster:
                case SacredScriptArgumentKind.CreatureNpcBehavior:
                case SacredScriptArgumentKind.CreatureStationary:
                    result.Add(new(kind, start));
                    break;
                case SacredScriptArgumentKind.TilePosition:
                case SacredScriptArgumentKind.TriggerFirstCorner:
                case SacredScriptArgumentKind.TriggerLastCorner:
                    var x = ReadInt(data, ref offset);
                    result.Add(x == SacredScriptSymbolicPositionArgumentLayout.SymbolicSentinel
                        ? new(kind, start, Text: ReadString(data, ref offset))
                        : new(kind, start, Position: new(x, ReadInt(data, ref offset), ReadInt(data, ref offset))));
                    break;
                case SacredScriptArgumentKind.TriggerPatchPosition:
                case SacredScriptArgumentKind.WorldPosition:
                    result.Add(new(kind, start, Position: new(ReadInt(data, ref offset), ReadInt(data, ref offset), ReadInt(data, ref offset))));
                    break;
                default:
                    throw new InvalidDataException($"Unimplemented operand 0x{(byte)kind:X2} at +0x{start:X}.");
            }
        }
        return result.AsReadOnly();
    }

    private static int ReadInt(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4) throw new InvalidDataException($"Truncated integer at +0x{offset:X}.");
        var value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        var length = data[offset..].IndexOf((byte)0);
        if (length < 0) throw new InvalidDataException($"Unterminated string at +0x{offset:X}.");
        var value = Encoding.Latin1.GetString(data.Slice(offset, length));
        offset += length + 1;
        return value;
    }
}
