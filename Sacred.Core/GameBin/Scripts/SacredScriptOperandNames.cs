namespace Sacred.Core.GameBin.Scripts;

/// <summary>Original script vocabulary recovered by the owner-provided Armalion editor.
/// Names describe encoded values; they do not imply host execution support.</summary>
public static class SacredScriptOperandNames
{
    private static readonly string[] Names =
    [
        "Nix", "SetName", "SetType", "SetDir", "SetPos", "OnDeath",
        "Lock", "Unlock", "Enemy", "DlgName", "DlgNum", "SetNum",
        "SetPos1", "SetPos2", "Friend", "Open", "Close", "Group",
        "Attack", "Join", "Defend", "QuestPool", "Text", "Resv1",
        "Resv2", "SetTypePrio", "Geisel", "Schatz", "SetQuestPool", "TextIntRes",
        "TextTxtRes", "SetLevel", "SetWPos", "CreateOEN", "DestroyOEX", "Blacksmith",
        "Merchant", "Master", "TrgOpen", "TrgClose", "TriggerType", "UseTrigger",
        "LPos", "Bauer", "TrgLock", "TrgUnlock", "FarmTier", "WindBusch",
        "Kleinvieh", "Kleinwild", "Grosswild", "SetMMLevel", "SecPos", "MinMaxEnemyArea",
        "GroupRad", "Follow", "SetDate", "UnFollow", "HasItem", "QuestSolved",
        "FlyTo", "DMG", "IsDead", "ToInv", "HasFollow", "OnTake",
        "Stadt", "QNPC", "QNPCOUT", "DoNotClean", "Soldat", "IsVar",
        "IsVarEq", "IsVarBit", "IsNotVarBit", "Hireling", "NPC", "SetHome",
        "IsDay", "IsNight", "AttackMe", "Kill", "IsHero", "RandomValues",
        "RandomWeapon", "RandomShield", "RandomArmour", "MatchHero", "WhileAnimMode", "WhileIsFighting",
        "WhileIsAlive", "WhileIsIdle", "WhileIsNotIdle", "WhileVarBit", "WhileNotVarBit", "WhileTimer",
        "OnSee", "QuestItem", "MainQuestItem", "SetOwner", "HorseMerchant", "UnHireling",
        "Once", "SpellPref", "Musik", "Region", "Wetter", "God",
        "ChestType", "IsVarUPR", "IsVarLWR", "HasMinExp", "DQ_Target", "Begleite",
        "DQ_Kill", "Taktik", "Still", "SetQuestNum", "SetQuestToDo", "IsFighting",
        "IsDlgNPCLoop", "Partikel", "IsInRgn", "Fix", "Append", "IsNPC",
        "Height", "Sternchen", "CleanNow", "IsLocked", "IsNotLocked", "OnUse",
        "TimeDmg", "Vampir", "RandomBooks", "Weapon", "Shield", "Armour",
        "Gauckler", "Damage", "Drunken", "DaemonSummon", "NotToInv", "IsDlgOwner",
        "SetHorseClass",
    ];

    public static string GetName(byte value)
        => value < Names.Length ? Names[value] : $"tag_0x{value:X2}";
}
