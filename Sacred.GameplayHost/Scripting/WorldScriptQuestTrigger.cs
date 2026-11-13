using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x48F030 startup selector/cache and empty-result quest entry.
/// Pending compass scratch, hero checkpoint/UI and live notifications require their own
/// lifecycle. Entry mutations precede OnEnter callback execution.</summary>
public static class WorldScriptQuestTrigger
{
    public static void Apply(WorldScriptExecutionContext context,SacredScriptCommand command,
        Func<int,bool> predicate,Action<int> invoke)
        => Apply(context,command,context.IsExecutingStartup,predicate,invoke);
    public static void Apply(WorldScriptExecutionContext context,SacredScriptCommand command,bool startup,
        Func<int,bool> predicate,Action<int> invoke)
    {
        if (!startup || !context.Multiplayer)
            throw new NotSupportedException("TriggerQuest requires its compared multiplayer startup caller.");
        var quests = context.Quests ?? throw new InvalidDataException("TriggerQuest requires the original quest table.");
        var number = -1; var name = "";
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.Integer)
                number = operand.Integer ?? throw new NotSupportedException("TriggerQuest numeric expressions require native resolution.");
            else if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                name = operand.Text!;
                if (name.Length >= SacredScriptQuestLayout.NameLength || name.Contains('$') ||
                    name.Contains("+Var(",StringComparison.Ordinal) || name.StartsWith("res:",StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("TriggerQuest name requires its original normalization.");
            }
        }
        if (name.Length == 0 && number == -1) return;
        var slot = quests.FindNumber(number);
        if (slot <= 0) slot = Enumerable.Range(0,quests.Count).FirstOrDefault(index=>name.Length!=0 &&
            WorldScriptNameComparer.Instance.Equals(quests.Get(index).Name,name),-1);
        if (slot <= 0 || (quests.Get(slot).Flags & SacredScriptQuestLayout.ActiveFlag) == 0) return;
        var callback = quests.Get(slot).ActivationFunction;
        if (callback == 0)
        {
            var expected = $"QIS_Trigger{quests.Get(slot).Number}";
            callback = Enumerable.Range(0,context.Functions.Count).FirstOrDefault(index=>
                WorldScriptNameComparer.Instance.Equals(context.Functions[index].Name,expected),-1);
            quests.WriteWord(slot,SacredScriptQuestLayout.ActivationFunctionOffset,unchecked((uint)callback));
        }
        if (!predicate(callback)) return;
        Enter(quests,slot,context.LocalPlayerSlot,context.HasActiveHero,invoke);
    }
    public static void Enter(WorldScriptQuests quests,int slot,uint? localPlayerSlot,bool? hasActiveHero,Action<int> invoke)
    {
        if ((quests.Get(slot).Flags & SacredScriptQuestLayout.TriggeredFlag) == 0)
        {
            if (hasActiveHero != false) throw new NotSupportedException("Quest entry requires its compared absent-hero startup state.");
            if (quests.Scratch is null || quests.Scratch.Any(value=>value!=0))
                throw new NotSupportedException("Quest entry requires established zero pending compass scratch or its native compass/UI lifecycle.");
            if (localPlayerSlot is not { } owner) throw new InvalidDataException("Quest entry needs the local controller slot.");
            quests.Journal.AppendTriggerEntry(quests.Get(slot).Number,owner);
            quests.WriteWord(slot,SacredScriptQuestLayout.FlagsOffset,quests.Get(slot).Flags | SacredScriptQuestLayout.TriggeredFlag);
            invoke(quests.Get(slot).EnterFunction);
        }
        quests.ClearScratch();
    }
}
