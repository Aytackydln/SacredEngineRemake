using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Complete supported CreateObj operands. Symbolic positions use current execution state;
/// unsupported effects fail before any reference is consumed or object is created.</summary>
public static class SacredScriptObjectCreationReader
{
    public static SacredScriptCreateObject Read(SacredScriptCommand command, Func<string, SacredScriptPosition> resolvePosition)
    {
        ArgumentNullException.ThrowIfNull(resolvePosition);
        if (command.Opcode != SacredScriptCommandHeaderLayout.CreateObjectOpcode)
            throw new ArgumentException("Expected CreateObj.", nameof(command));
        string? name = null, trigger = null, take = null, use = null;
        uint type = 0;
        SacredScriptPosition? tile = null, world = null;
        ushort? facing = null;
        short? height = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TypeId: type = unchecked((uint)operand.Integer!.Value); break;
                case SacredScriptArgumentKind.NullTerminatedString:
                    if (string.IsNullOrEmpty(name)) name = Text(operand);
                    else throw new InvalidDataException("Repeated object name requires native resource/association resolution.");
                    break;
                case SacredScriptArgumentKind.ObjectReference: trigger = (trigger ?? "") + Text(operand); break;
                case SacredScriptArgumentKind.ObjectTakeScript: take = Text(operand); break;
                case SacredScriptArgumentKind.ObjectUseScript: use = Text(operand); break;
                case SacredScriptArgumentKind.Facing: facing = checked((ushort)operand.Integer!.Value); break;
                case SacredScriptArgumentKind.HeightOffset: height = checked((short)operand.Integer!.Value); break;
                case SacredScriptArgumentKind.TilePosition:
                case SacredScriptArgumentKind.TriggerFirstCorner:
                case SacredScriptArgumentKind.TriggerLastCorner:
                    tile = operand.Position ?? resolvePosition(Text(operand));
                    break;
                case SacredScriptArgumentKind.WorldPosition: world = operand.Position; break;
                default: throw new InvalidDataException($"CreateObj effect 0x{(byte)operand.Kind:X2} is unresolved at +0x{operand.Offset:X}.");
            }
        }
        if (tile is not null && world is not null)
            throw new InvalidDataException("Combined tile/world placement requires native offset semantics.");
        return new(name, type, tile, world, height, FacingDegrees: facing, TriggerReference: trigger)
        { TakeScript = take, UseScript = use };
    }

    private static string Text(SacredScriptOperand operand)
    {
        var value = operand.Text ?? throw new InvalidDataException($"Expected text at +0x{operand.Offset:X}.");
        if (value.Contains('$') || value.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"CreateObj name '{value}' requires the native resource resolver.");
        return value;
    }
}
