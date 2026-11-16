namespace Sacred.Network.Admission;

/// <summary>Gold FIRSTCONTACT response payload. The native +2 UInt32 is stored
/// as controller +0x24 and used as a hosted-server window target by local 0x117;
/// it is not an actor reference or a process handle.</summary>
public static class GoldAdmissionResponseLayout
{
    public const int Size = 6;
    public const int FlagsOffset = 0;
    public const int PlayerSlotOffset = 1;
    public const int HostedServerWindowOffset = 2;
}
