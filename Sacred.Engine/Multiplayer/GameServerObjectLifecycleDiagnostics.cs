using System.Collections.Generic;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal static class GameServerObjectLifecycleDiagnostics
{
    public static void Apply(Dictionary<uint, uint[]> movements, Dictionary<uint, bool> doors, GoldGameEvent entry)
    {
        switch (entry)
        {
            case GoldObjectRemovalEvent removal:
                movements.Remove(removal.ObjectReference);
                doors.Remove(removal.ObjectReference);
                EngineLog.WriteLine($"Network removal: ref {removal.ObjectReference}, {(removal.Immediate ? "immediate" : "ordinary")} notification; inspect current replica flags.");
                break;
            case GoldObjectOwnerEvent owner:
                EngineLog.WriteLine($"Network ownership: ref {owner.ObjectReference}, owner {owner.Owner}; raw word 0x{owner.OwnerWord:X8}.");
                break;
        }
    }
}
