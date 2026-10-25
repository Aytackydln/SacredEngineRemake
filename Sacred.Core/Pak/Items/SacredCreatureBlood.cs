namespace Sacred.Core.Pak.Items;

/// <summary>Gold TypeManager 427160: category-checked Items.pak blood palette.</summary>
public static class SacredCreatureBlood
{
    public static uint Color(in ItemsPakEntryModelDescLayout item) => (byte)item.Category is 2 or 3 or 32
        ? item.GoreType switch
        {
            1 => 0xFF00AA00,
            2 => 0xFF111111,
            3 => 0xFF735623,
            4 => 0x80ABBECD,
            _ => 0xFFAA0000
        }
        : 0xFF000000;
}
