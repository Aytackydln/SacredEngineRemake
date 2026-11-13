using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4AE707 skips InfoPlayer selector 5 in multiplayer.
/// Its offline local GUI event and other selector effects require presentation services.</summary>
public static class WorldScriptPlayerInformation
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var values = new List<int>();
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind != SacredScriptArgumentKind.Integer || operand.Integer is not { } number)
                throw new NotSupportedException("InfoPlayer requires its compared literal numeric caller.");
            values.Add(number);
        }
        if (values.Count == 0) return;
        if (values.Count > 10 || values[0] != 5 || !context.Multiplayer)
            throw new NotSupportedException("InfoPlayer requires native selector/UI/sound effects outside multiplayer selector 5.");
    }
}
