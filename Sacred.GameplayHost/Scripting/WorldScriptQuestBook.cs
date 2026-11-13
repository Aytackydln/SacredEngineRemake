using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x496080 startup QuestBook: update the first journal row for
/// its number, selecting title every tenth mode or the first of ten empty texts.
/// Startup suppresses individual publication; no record is inserted here.</summary>
public static class WorldScriptQuestBook
{
    public static void Apply(WorldScriptExecutionContext context,SacredScriptCommand command)
    {
        if(!context.IsExecutingStartup)throw new NotSupportedException("QuestBook live entry requires native journal publication.");
        var quests=context.Quests??throw new InvalidDataException("QuestBook requires the original journal state.");
        var number=-1;var mode=-1;uint resource=0;
        foreach(var operand in SacredScriptOperandReader.Read(command))
        {
            if(operand.Kind==SacredScriptArgumentKind.Integer)
            {
                var value=operand.Integer??throw new NotSupportedException("QuestBook numeric expressions require native resolution.");
                if(number==-1)number=value;else mode=value;
            }
            else if(operand.Kind==SacredScriptArgumentKind.NullTerminatedString)
                resource=WorldScriptResourceReference.Resolve(operand.Text!,context.ResourceText??throw new InvalidDataException("QuestBook requires original resource text lookup."));
            else throw new NotSupportedException("QuestBook requires its compared literal number/mode/resource caller.");
        }
        quests.Journal.UpdateBook(number,mode,resource);
    }
}
