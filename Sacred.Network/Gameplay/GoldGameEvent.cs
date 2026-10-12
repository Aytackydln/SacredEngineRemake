using Sacred.Network.Admission;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldGameEvent(GoldMessage Message);

public sealed record GoldFirstContactEvent(GoldMessage Message, GoldFirstContact Request) : GoldGameEvent(Message);
public sealed record GoldPlayerDataEvent(GoldMessage Message, GoldPlayerData PlayerData) : GoldGameEvent(Message);
public sealed record GoldPlayerRosterEvent(GoldMessage Message, GoldPlayerRoster Roster) : GoldGameEvent(Message);
public sealed record GoldPasswordResultEvent(GoldMessage Message, bool Accepted) : GoldGameEvent(Message);
public sealed record GoldTimeProbeEvent(GoldMessage Message, uint Word0, uint Word1, uint Word2, uint Word3) : GoldGameEvent(Message);
public sealed record GoldReferenceBatchEvent(GoldMessage Message, GoldReferenceBatch Batch) : GoldGameEvent(Message);
public sealed record GoldReferenceRequestEvent(GoldMessage Message, uint Count) : GoldGameEvent(Message);
/// <summary>Client 0x7E4524 stores Start and unchecked Start+Count as native reference bounds.</summary>
public sealed record GoldReferenceRangeEvent(GoldMessage Message, uint Start, uint Count) : GoldGameEvent(Message)
{
    public uint EndExclusive => unchecked(Start + Count);
}

/// <summary>FIRSTCONTACT response at 0x4EBDE6: flags, Sacred slot, opaque native host token.</summary>
public sealed record GoldAdmissionEvent(GoldMessage Message, byte Flags, byte PlayerSlot, uint HostToken) : GoldGameEvent(Message);
public sealed record GoldRejectionEvent(GoldMessage Message, uint Reason, uint DataMismatchMask) : GoldGameEvent(Message);

/// <summary>Six-word prefix consumed by Gold's movement handlers. Word meanings remain build-specific.</summary>
public sealed record GoldMovementEvent(GoldMessage Message, uint ObjectReference,
    uint Word1, uint Word2, uint Word3, uint Word4, uint Word5, byte[] ObjectState) : GoldGameEvent(Message);
/// <summary>Opcode 0x162 consumes six words at 0x496AB0 and dispatches native event 0x203.</summary>
public sealed record GoldMovementEndEvent(GoldMessage Message, uint ObjectReference,
    uint Word1, uint Word2, uint Word3, uint Word4, uint Word5, byte[] RemainingData) : GoldGameEvent(Message);

/// <summary>Opcode 0x164 carries a reference and clears native action fields at 0x4C60DA.</summary>
public sealed record GoldIdleEvent(GoldMessage Message, uint ObjectReference, byte[] RemainingData) : GoldGameEvent(Message);
public sealed record GoldDoorEvent(GoldMessage Message, uint ObjectReference, bool Open, byte[] RemainingData) : GoldGameEvent(Message);

/// <summary>Verified object prefix and base state; native subtype bytes remain opaque.</summary>
public sealed record GoldObjectEvent(GoldMessage Message, uint ObjectReference, uint ObjectType,
    uint[] PositionWords, byte[] ObjectState) : GoldGameEvent(Message)
{
    public GoldObjectBaseState BaseState => GoldObjectBaseState.FromPayload(ObjectState);
    public GoldObjectPosition? Position => PositionWords.Length == 4
        ? new(unchecked((int)PositionWords[0]), unchecked((int)PositionWords[1]), PositionWords[2], PositionWords[3])
        : null;
}
public sealed record GoldUnknownEvent(GoldMessage Message) : GoldGameEvent(Message);
