using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Gold SetTrigger operands and state-mask order. Creation itself requires a world and trigger grant.</summary>
public static class SacredScriptTriggerCreationReader
{
    public static SacredScriptTriggerCreation? Read(SacredScriptCommand command)
    {
        if (command.Opcode != SacredScriptTriggerOpcodes.SetTrigger) throw new ArgumentException("Expected SetTrigger.", nameof(command));
        var name = SacredScriptTriggerOpcodes.NamePrefix;
        byte? scope = null;
        uint? state = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.NullTerminatedString:
                    name += operand.Text;
                    break;
                case SacredScriptArgumentKind.TriggerScope:
                    scope = checked((byte)operand.Integer!.Value);
                    break;
                case SacredScriptArgumentKind.TriggerStateOn: state = (state ?? 0) | 1; break;
                case SacredScriptArgumentKind.TriggerStateOff: state = (state ?? 0) & ~1u; break;
                case SacredScriptArgumentKind.TriggerState8000On: state = ((state ?? 0) & ~1u) | 0x8000; break;
                case SacredScriptArgumentKind.TriggerState8000Off: state = (state ?? 0) & ~0x8000u; break;
            }
        }
        return name.Length == SacredScriptTriggerOpcodes.NamePrefix.Length || scope is null || state is null ? null :
            new(name, scope.Value, unchecked((ushort)state.Value), command.FileOffset);
    }
}
