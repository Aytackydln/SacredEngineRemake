using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC's explicit dialog selector and startup attachment.
/// Registration slots are distinct from function indices and opcode-106 associations.</summary>
public static class WorldCreatureScriptDialog
{
    public static int Resolve(WorldScriptRegistrationTable table, SacredScriptDialogSelection? operand)
    {
        if (operand is null) return -1;
        if (operand.Number == 0) return 0;
        for (var index = 0; index < table.Count; index++)
        {
            var bytes = table.GetRecord(index);
            if (operand.Number is not null)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) == operand.Number.Value) return index;
            }
            else
            {
                var name = bytes.AsSpan(SacredScriptDialogLayout.NameOffset, SacredScriptDialogLayout.NameLength);
                var end = name.IndexOf((byte)0);
                if (WorldScriptNameComparer.Instance.Equals(Encoding.Latin1.GetString(end < 0 ? name : name[..end]), operand.Name!)) return index;
            }
        }
        return -1;
    }

    public static WorldCreatureInitialActor ApplyInitial(WorldCreatureInitialActor actor,
        WorldScriptExecutionContext context, SacredScriptDialogSelection? operand)
    {
        var selected = Resolve(context.Registrations, operand);
        if (selected <= 0) return actor;
        if (!context.IsExecutingStartup && !context.IsInitializingRegion)
            throw new NotSupportedException("Live dialog binding requires its notification lifecycle.");
        return BindInitial(actor, context.Registrations, selected);
    }

    public static WorldCreatureInitialActor BindInitial(WorldCreatureInitialActor actor, WorldScriptRegistrationTable table, int selected)
    {
        if (selected <= 0) return actor;
        table.AttachInitial(actor.Base.ObjectReference, actor.DialogIndex, selected);
        return actor with { DialogIndex = (uint)selected, Base = actor.Base with { Flags = actor.Base.Flags | 0x80000 } };
    }
}
