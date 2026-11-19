using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Reached ordinary single-bit clear in EnterRegion. Actor/resource
/// families, expressions and other caller scopes keep their existing guards.</summary>
internal static class WorldScriptEnteredVariableBits
{
    public static bool Supports(SacredScriptCommand command)
    {
        if(command.Opcode!=SacredScriptExecutionOpcodes.ClearVariableBits)return false;
        var values=SacredScriptOperandReader.Read(command);
        return values.Count==2 && values[0].Kind==SacredScriptArgumentKind.NullTerminatedString &&
            values[0].Text is {Length:>0 and <32} name && !name.Contains('$') && !name.Contains('+') && !name.Contains(':') &&
            !WorldScriptNameComparer.Instance.Equals(name,"HeroQBit") &&
            values[1].Kind==SacredScriptArgumentKind.Integer && values[1].Integer is >=0 and <=31;
    }
    public static void Apply(WorldScriptExecutionContext context,SacredScriptCommand command)
    {
        if(!context.IsEnteringRegion || context.IsExecutingStartup || !context.Multiplayer || !Supports(command))
            throw new NotSupportedException("Variable clear bits require the compared ordinary EnterRegion caller.");
        var values=SacredScriptOperandReader.Read(command);
        context.Variables.ClearEnteredBit(values[0].Text!,values[1].Integer!.Value,
            checked((byte)(context.LocalPlayerSlot ?? throw new InvalidDataException("Variable publication needs its actual owner."))));
    }
}
