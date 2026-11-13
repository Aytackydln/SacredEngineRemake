namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x484C43..0x484CE8. Usage 12/14 first requires motion 0x8B;
/// every ranged usage then requires 0x72. Missing motion must run native resource
/// selection before eligibility, allocation and generation.</summary>
public static class WorldCreatureRangedEquipment
{
    public static bool CanRetain(byte usage, Func<int, bool> motionAvailable)
    {
        if (usage is 12 or 14 && !motionAvailable(0x8b)) return false;
        return usage is not (7 or 12 or 13 or 14) || motionAvailable(0x72);
    }
}
