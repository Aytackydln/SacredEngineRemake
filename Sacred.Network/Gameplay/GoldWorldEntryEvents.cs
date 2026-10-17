using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWorldUploadEvent(GoldMessage Message, bool Host) : GoldGameEvent(Message);
public sealed record GoldParticipantStartRequestEvent(GoldMessage Message) : GoldGameEvent(Message);
/// <summary>Client 0x7E1A61: native participant start approval, distinct from actor/snapshot binding.</summary>
public sealed record GoldParticipantStartApprovedEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldHostStartRequestEvent(GoldMessage Message, uint Word) : GoldGameEvent(Message)
{
    public uint SectorTableSize => Word;
}
/// <summary>Server 0x18 is empty and may retain the host's Sacred slot in Origin.</summary>
public sealed record GoldHostWorldAvailableEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldPeerReadyEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldSectorEntryEvent(GoldMessage Message, GoldSectorEntry Entry) : GoldGameEvent(Message);
public sealed record GoldSectorInterestEvent(GoldMessage Message, GoldSectorInterestRequest Request) : GoldGameEvent(Message);
