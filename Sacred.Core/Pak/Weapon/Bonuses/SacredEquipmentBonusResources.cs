namespace Sacred.Core.Pak.Weapon.Bonuses;

// Native Gold bonus switch (0x5C48C0). Keys resolve against the loaded global.res.
internal static class SacredEquipmentBonusResources
{
    public static (string Key, bool Percent, bool HasValue)? Get(ushort code) => code switch
    {
        811 => ("1100", false, true),
        812 => ("1098", false, true),
        813 => ("1148", true, true),
        814 => ("1149", true, true),
        815 => ("1090", false, true),
        816 => ("1092", false, true),
        817 => ("1091", false, true),
        818 => ("1093", false, true),
        819 => ("1094", false, true),
        820 => ("1095", false, true),
        840 => ("1124", false, true),
        841 => ("1125", true, true),
        842 => ("1127", true, true),
        843 => ("1128", true, true),
        844 => ("1138", false, true),
        845 => ("1137", false, true),
        846 => ("1238", true, true),
        847 => ("1139", true, true),
        848 => ("1140", true, true),
        849 => ("1136", false, true),
        850 => ("1141", true, true),
        851 => ("1142", true, true),
        852 => ("1129", true, true),
        853 => ("1130", false, true),
        855 => ("1150", true, true),
        856 => ("1143", true, true),
        857 => ("1151", true, true),
        858 => ("UI_RARE_MAGIEABWEHR", false, true),
        859 => ("1132", false, true),
        860 => ("UI_RARE_SCHADEN", true, true),
        861 => ("UI_RARE_STONEWALL", false, true),
        862 => ("UI_RARE_SCHOCK", false, true),
        863 => ("UI_RARE_EXORZIST", false, true),
        864 => ("1144", false, false),
        865 => ("1133", false, true),
        866 => ("1134", true, true),
        867 => ("1135", true, true),
        868 => ("UI_RARE_ALCHEMIE", false, true),
        _ => null
    };
}
