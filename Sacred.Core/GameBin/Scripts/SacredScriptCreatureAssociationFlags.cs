namespace Sacred.Core.GameBin.Scripts;

/// <summary>CreateNPC parser scratch flags, rather than serialized actor flags.
/// Gold 0x483D1A gates quest-object association; 0x484778 gates ownership.</summary>
public static class SacredScriptCreatureAssociationFlags
{
    public const uint QuestObject = 1;
    public const uint QuestObjectFlag20 = 0x400;
    public const uint QuestObjectFlag40 = 0x800;
    public const uint QuestObjectFlag80 = 0x1000;
    public const uint QuestObjectMask = QuestObject | QuestObjectFlag20 | QuestObjectFlag40 | QuestObjectFlag80;
    public const uint Ownership = 0x100;
    public const uint HeroOwnership = Ownership | 0x200;
    /// <summary>Compared service flags supported by the startup factory. They
    /// enter neither of the native associated-creature branches above.</summary>
    public const uint StartupServicesMask = 0x3C;
}
