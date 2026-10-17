using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Reads SetIcon operands without running its surrounding quest or dialog.</summary>
public static class SacredScriptDialogIconReader
{
    public static SacredScriptDialogIconChange Read(SacredScriptCommand command, Func<string, int?>? variable = null)
    {
        if (command.Opcode != SacredScriptDialogIconLayout.Opcode) throw new ArgumentException("Expected SetIcon.", nameof(command));
        string? name = null;
        int? icon = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name = operand.Text;
            else if (operand.Kind == SacredScriptArgumentKind.Integer)
                icon = operand.Integer ?? variable?.Invoke(operand.Text!) ?? throw new InvalidDataException("Unresolved SetIcon variable.");
            else throw new InvalidDataException($"Unsupported SetIcon operand at +0x{operand.Offset:X}.");
        }
        return new(name, icon ?? throw new InvalidDataException("SetIcon has no selector."));
    }
}
