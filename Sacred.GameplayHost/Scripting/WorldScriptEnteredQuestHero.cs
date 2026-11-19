using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>46C3C5..46C420's current hero checkpoint refresh on quest entry.
/// Pending compass and OnEnter belong to the existing quest/journal owner.</summary>
internal static class WorldScriptEnteredQuestHero
{
    public static WorldCreatureInitialActor Prepare(WorldCreatureInitialActor actor, int questNumber, uint gameFlags, bool connected)
    {
        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(actor.KiState) == 10 && questNumber == 1)
            throw new NotSupportedException("Quest entry for AI mode 10 requires its original quest-one UI/action lifecycle.");
        if (questNumber >= 100 || !connected || (gameFlags & 0x40010) == 0) return actor;
        return actor with { ScriptInitialPosition = new GoldObjectPosition(actor.Base.X, actor.Base.Y, actor.Base.Surface, actor.Base.Sector) };
    }
}
