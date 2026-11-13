namespace Sacred.Core.GameBin.Scripts;

/// <summary>Original script vocabulary recovered by the owner-provided Armalion editor.
/// Names describe encoded values; they do not imply host execution support.</summary>
public static class SacredScriptOpcodeNames
{
    private static readonly string[] Names =
    [
        "Nix", "CreateNPC", "SetObjState", "SetNPCState", "SetBaseTrigger", "DelBaseTrigger",
        "TalkTo", "EquipNPC", "CreateObj", "StartScript", "QuestDone", "QuestNotDone",
        "QuestInProgress", "QuestNotInProgress", "DeleteFunktionBlock", "ExitQuest", "MoveOver", "AddExp",
        "AddGold", "AddGems", "TriggerQuest", "SetUpQuest", "CallFunktion", "DefPos",
        "GroupIsDead", "CheckForQuestPool", "Text", "MaxOffen", "Delay", "Rettungsmission",
        "ToDoBloecke", "Dialog", "Belohnungen", "HideTmpToDo", "Schatzsuche", "FillRegion",
        "AddHandel", "SetRG", "UnsetRG", "OnMoveOver", "SetDLGs", "OnQuestState",
        "Lable", "ExitDlg", "DelDlg", "StartPosition", "Teleport", "AddFunktion",
        "CreateTrigger", "SetTriggerState", "TriggerPatch", "FillSector", "SetHP", "QuestBook",
        "LoseQuest", "DelNPC", "SetTimer", "DelTimer", "IF", "ELSE",
        "SetButton", "ChDlgText", "NOP", "QuestKompassPos", "QuestKompassObj", "DefNum",
        "ELSEIF", "SetVar", "SetVarBit", "UnsetVarBit", "SetFocus", "NPC_TalkTo",
        "NPC_Goto", "SetGroupState", "GroupGoto", "IncVar", "DecVar", "UnTriggerQuest",
        "FillChest", "Morph", "SetST", "SetGS", "SetWI", "SetRP",
        "SetRM", "SetCH", "SetIcon", "SetQuestInfo", "SetAnimMode", "SetPlayMode",
        "Wait", "PlayAnim", "Attack", "ActObj", "MouseEvent", "POPUP",
        "ReplaceSpawn", "SetMapIcon", "SecInfo", "Partikel", "SpawnValues", "Toete_NPC",
        "Begleite", "SetCV", "PlaySound", "RndVar", "SetRgnDialog", "BalanceSpawnItem",
        "AddPoolPos", "GetPoolPos", "RenPos", "ToBegin", "AddPoolRgn", "GetPoolRgn",
        "Waffenpool", "MakeGroup", "RegionChange", "ActivateQuest", "SetClientVars", "GetClientVars",
        "GameEnd", "AutoSave", "SolveTime", "Strafen", "RegionFrei", "Belohnung",
        "RejectQuest", "PlayMusic", "PlayJingle", "SetScriptSoundControle", "Teleporter", "GameActive",
        "InfoPlayer",
    ];

    public static string GetName(ushort value)
        => value < Names.Length ? Names[value] : $"op_0x{value:X2}";
}
