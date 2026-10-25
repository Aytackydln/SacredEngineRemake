namespace Sacred.Core.Runtime;

/// <summary>Gold 42363C..42368A default class hostility, with 4238C2..423904 inversion.
/// The original 16-by-16 relation table at 890A30 is copied by 422820.
/// Scripted relation-table edits, individual exceptions and multiplayer teams are separate.</summary>
public static class SacredCreatureRelations
{
    public static bool AreDefaultEnemies(uint firstClass, uint secondClass, uint firstBehavior, uint secondBehavior)
    {
        // Bit n denotes a zero (hostile) entry in the executable's original table.
        ReadOnlySpan<ushort> hostile = [0x2000, 0xFF24, 0x208A, 0xFF24,
            0xFF24, 0x318A, 0x2000, 0xFF24, 0x20AA, 0xA08A, 0x208A, 0x208A,
            0x20AA, 0xFFFF, 0x208A, 0x23AA];
        if (firstClass >= hostile.Length || secondClass >= hostile.Length) return false;
        var enemy = (hostile[(int)firstClass] & (1 << (int)secondClass)) != 0;
        return ((firstBehavior | secondBehavior) & HostilityInvertedFlag) != 0 ? !enemy : enemy;
    }

    public const uint HostilityInvertedFlag = 0x40000;
}
