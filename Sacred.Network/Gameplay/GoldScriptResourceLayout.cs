namespace Sacred.Network.Gameplay;

/// <summary>Gold script-name table at interpreter +0x765C and resource/object map at +0x7668.</summary>
public static class GoldScriptResourceLayout
{
    public const ushort RemoveNameOpcode = 0x1B6;
    public const ushort RegisterNameOpcode = 0x1B7;
    public const int NameBankSection = 4;
    public const int BindingBankSection = 6;
    public const int NameSize = 64;
    public const int NameRecordSize = 72;
    public const int TriggerReferenceOffset = 64;
    public const int ResourceIdOffset = 68;
    /// <summary>Gold 0x46517A..0x4651CF scans map keys 8310..8398, irrespective of map values.</summary>
    public const uint FirstLocalObjectResource = 0x2076;
    public const uint LocalObjectResourceLimit = 0x20CF;
    public const uint UnnamedResource = uint.MaxValue;
    public const string LocalResourcePrefix = "LRes:";
}
