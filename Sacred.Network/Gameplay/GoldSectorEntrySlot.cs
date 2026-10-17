namespace Sacred.Network.Gameplay;

/// <summary>One native sector-entry slot. Mode/flag bytes retain noncanonical nonzero values.</summary>
public readonly record struct GoldSectorEntrySlot(ushort Sector, ushort RetainedWord, byte ScriptMode, byte InitializeFlag)
{
    public bool IsPresent => Sector != 0;
    public bool RequestsInitialization => IsPresent && InitializeFlag != 0;
    public bool RequestsScripts => IsPresent && ScriptMode != 0;
}
