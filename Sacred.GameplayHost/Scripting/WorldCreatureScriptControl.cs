using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using System.Buffers.Binary;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC relation selection (0x483605..0x4836B8), then
/// behavior/status writes (0x4839F2..0x483A32). No active hero, resolved explicit
/// behavior including initial bit-1 AI state. This selects relation class,
/// not KI mode; 0x52E420(1,class) only assigns native +0x1F0.</summary>
public static class WorldCreatureScriptControl
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, uint behavior, int aggression, int flagInput)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var relation = actor.RelationClass;
        var runtime = actor.RuntimeFlags;
        if (aggression >= 0 || (behavior & 0xc000) != 0)
        {
            var mercenary = (behavior & 0x4000) != 0 || relation == (uint)SacredCreatureClass.Mercenary;
            relation = (uint)(aggression == 1 ? (mercenary ? SacredCreatureClass.Human : SacredCreatureClass.Monster)
                : (mercenary ? SacredCreatureClass.Mercenary : SacredCreatureClass.Npc));
            runtime &= ~0x40u;
        }
        var status = flagInput == 0 ? (ushort)(actor.Flags2B7 & ~8) : flagInput > 0 ? (ushort)(actor.Flags2B7 | 8) : actor.Flags2B7;
        var ai = new SacredCreatureInitialAiState(BinaryPrimitives.ReadUInt16LittleEndian(actor.KiState),
            BinaryPrimitives.ReadUInt16LittleEndian(actor.KiState.AsSpan(2)), actor.ThinkInterval, actor.ThinkElapsed)
            .ApplyWithoutHero(runtime, behavior);
        var ki = actor.KiState.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(ki, ai.Mode);
        BinaryPrimitives.WriteUInt16LittleEndian(ki.AsSpan(2), ai.State);
        return actor with { KiState = ki, ThinkInterval = ai.Interval, ThinkElapsed = ai.Elapsed, RelationClass = relation, RuntimeFlags = runtime, BehaviorFlags = behavior, Flags2B7 = status };
    }
}
