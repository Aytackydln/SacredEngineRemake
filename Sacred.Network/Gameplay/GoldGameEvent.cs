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
/// <summary>Client 0x7E4524 stores Start and unchecked Start+Count as world-trigger bounds,
/// consumed by 0x6387B0. Object references use the distinct 0x153 vector.</summary>
public sealed record GoldReferenceRangeEvent(GoldMessage Message, uint Start, uint Count) : GoldGameEvent(Message)
{
    public uint EndExclusive => unchecked(Start + Count);
}

/// <summary>FIRSTCONTACT response at 0x4EBDE6: flags, Sacred slot, opaque native host token.</summary>
public sealed record GoldAdmissionEvent(GoldMessage Message, byte Flags, byte PlayerSlot, uint HostToken) : GoldGameEvent(Message);
public sealed record GoldRejectionEvent(GoldMessage Message, uint Reason, uint DataMismatchMask) : GoldGameEvent(Message);

/// <summary>Gold start/update prefix: reference, packed +0x174 control, current X/Y, surface and sector.</summary>
public sealed record GoldMovementEvent(GoldMessage Message, uint ObjectReference,
    uint Word1, uint Word2, uint Word3, uint Word4, uint Word5, byte[] ObjectState) : GoldGameEvent(Message)
{
    public uint Control174 => Word1;
    public GoldObjectPosition Position => new(unchecked((int)Word2), unchecked((int)Word3), Word4, Word5);
    public GoldMovementBody? Body => ObjectState.Length == 0 ? null : GoldMovementBody.FromPayload(Message.Opcode, ObjectState);
}
/// <summary>Opcode 0x162 consumes six words at 0x496AB0 and dispatches native event 0x203.</summary>
public sealed record GoldMovementEndEvent(GoldMessage Message, uint ObjectReference,
    uint Word1, uint Word2, uint Word3, uint Word4, uint Word5, byte[] RemainingData) : GoldGameEvent(Message)
{
    public GoldObjectPosition Position => new(unchecked((int)Word1), unchecked((int)Word2), Word3, Word4);
    public uint FacingBits => Word5;
}

/// <summary>Opcode 0x164 carries a reference and clears native action fields at 0x4C60DA.</summary>
public sealed record GoldIdleEvent(GoldMessage Message, uint ObjectReference, byte[] RemainingData) : GoldGameEvent(Message);
public sealed record GoldDoorEvent(GoldMessage Message, uint ObjectReference, bool Open, byte[] RemainingData) : GoldGameEvent(Message);

/// <summary>Verified object prefix and base state; native subtype bytes remain opaque.</summary>
public sealed record GoldObjectEvent(GoldMessage Message, uint ObjectReference, uint ObjectType,
    uint[] PositionWords, byte[] ObjectState) : GoldGameEvent(Message)
{
    public GoldObjectBaseState BaseState => GoldObjectBaseState.FromPayload(ObjectState);
    public GoldPhysicalState? PhysicalState => GoldObjectStateReader.ReadPhysical(ObjectState);
    public GoldCreatureState? CreatureState => GoldObjectStateReader.ReadCreature(ObjectState);
    public GoldItemState? ItemState => GoldObjectStateReader.ReadItem(ObjectState);
    public GoldObjectFxState? FxState => GoldObjectStateReader.ReadFx(ObjectState);
    public GoldCreatureTail? CreatureTail => CreatureState?.ReadTail(Message.Opcode);
    public GoldObjectPosition? Position => PositionWords.Length == 4
        ? new(unchecked((int)PositionWords[0]), unchecked((int)PositionWords[1]), PositionWords[2], PositionWords[3])
        : null;
}
public sealed record GoldUnknownEvent(GoldMessage Message) : GoldGameEvent(Message);
