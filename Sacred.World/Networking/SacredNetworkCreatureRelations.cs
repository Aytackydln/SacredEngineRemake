using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Gold 423580 relationship query used by health rendering (548F60).
/// Keeps native instance exceptions out of generic archive readers.</summary>
public static class SacredNetworkCreatureRelations
{
    public static bool AreEnemies(GoldWorldReplica world, GoldReplicatedObject first, GoldReplicatedObject second)
    {
        if (first.Creature is not { } a || second.Creature is not { } b ||
            first.BaseState.ObjectReference == second.BaseState.ObjectReference) return false;
        var enemy = SacredCreatureRelations.AreDefaultEnemies(a.RelationClass, b.RelationClass, 0, 0);
        var rider = Class(second) == SacredCreatureClass.Horse
            ? world.Objects.GetValueOrDefault(b.EquipmentReferences[18]) : null;
        if (!enemy)
        {
            var at = first.BaseState.ObjectType; var bt = second.BaseState.ObjectType;
            // These type-pair exceptions are explicit branches at 423696..42370C.
            if ((at is 189 or 334 && bt is 245 or 188) || (bt is 189 or 334 && at is 245 or 188) ||
                (at == 75 && bt is not (75 or 99 or 109)) || (bt == 75 && at is not (75 or 99 or 109))) enemy = true;
            if ((a.Word200 & GoldCreatureStateLayout.UnlinkedAnimalHostilityFlag) != 0 && Class(second) == SacredCreatureClass.Animal &&
                !Linked(first, second) && unchecked((int)b.Word251) <= 0) enemy = true;
            if ((b.Word200 & GoldCreatureStateLayout.UnlinkedAnimalHostilityFlag) != 0 && Class(first) == SacredCreatureClass.Animal &&
                !Linked(second, first) && unchecked((int)a.Word251) <= 0) enemy = true;
            if (rider?.Creature is { } riding)
                enemy = SacredCreatureRelations.AreDefaultEnemies(a.RelationClass, riding.RelationClass, 0, 0);
        }
        // Gold 7DB280 reads connection game flag 0x40. This early team result
        // precedes the ordinary hostility inversion at 4238C2.
        if ((world.Session.GameFlags.GetValueOrDefault() & GoldSessionGameFlagsCodec.PlayerVersusPlayerFlag) != 0 &&
            PlayerControlled(first) && (PlayerControlled(second) || rider?.BaseState.ObjectReference is <= 16))
            return !SameTeam(Owner(first), Owner(rider?.BaseState.ObjectReference is <= 16 ? rider : second));
        return ((a.BehaviorFlags | b.BehaviorFlags) & SacredCreatureRelations.HostilityInvertedFlag) != 0 ? !enemy : enemy;

        SacredCreatureClass? Class(GoldReplicatedObject actor) => world.CreatureClassForType(actor.BaseState.ObjectType)
            ?? (actor.Creature?.RelationClass is <= 15 ? (SacredCreatureClass)actor.Creature.RelationClass : null);
        bool SameTeam(uint left, uint right) => left == right ||
            world.Session.Teams.GetTeam(left) is var team && team != 0 && team == world.Session.Teams.GetTeam(right);
    }

    private static bool PlayerControlled(GoldReplicatedObject actor) => actor.BaseState.ObjectReference <= 16 ||
        (actor.Creature!.Word200 & GoldCreatureStateLayout.PlayerControlledFlag) != 0;
    private static uint Owner(GoldReplicatedObject actor) => actor.BaseState.ObjectReference > 16 &&
        (actor.Creature!.BehaviorFlags & 4) != 0 ? actor.Creature.Word251 : actor.BaseState.ObjectReference;
    private static bool Linked(GoldReplicatedObject owner, GoldReplicatedObject subject)
    {
        if (owner.LinkedCreatures is not { } links) return false;
        for (var i = 0; i < links.Count; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(links.GetRecord(i)) == subject.BaseState.ObjectReference) return true;
        return false;
    }
}
