namespace Sacred.Network.Admission;

/// <summary>Gold NETWORK_SPEEDSETTINGS sent at FIRSTCONTACT +8. Only value 1 enables reduced NPC detail.</summary>
public enum GoldConnectionMode : byte
{
    LowBandwidth = 1,
    LanHighBandwidth = 2
}
