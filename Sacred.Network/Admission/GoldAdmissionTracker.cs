using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Admission;

/// <summary>Serializes sender/receiver transitions and exposes immutable admission status to observers.</summary>
public sealed class GoldAdmissionTracker
{
    private GoldAdmissionStatus _status = new(GoldAdmissionPhase.NotRequested);
    private readonly object _gate = new();

    public GoldAdmissionStatus Status => Volatile.Read(ref _status);

    public void Begin()
    {
        lock (_gate)
        {
            if (Status.Phase != GoldAdmissionPhase.NotRequested)
                throw new InvalidOperationException("FIRSTCONTACT has already been requested.");
            Volatile.Write(ref _status, new(GoldAdmissionPhase.WaitingForResponse)
            { SessionLocked = Status.SessionLocked, SessionGameFlags = Status.SessionGameFlags,
                AnnouncedMaxPlayers = Status.AnnouncedMaxPlayers, MaxPlayers = Status.MaxPlayers,
                SessionPaused = Status.SessionPaused, PausedBy = Status.PausedBy });
        }
    }

    public void BeginPassword()
    {
        lock (_gate)
        {
            var status = Status;
            if (status.Phase != GoldAdmissionPhase.SlotAssigned || status.Password == GoldPasswordStatus.WaitingForResponse)
                throw new InvalidOperationException("Password submission requires an assigned slot and no outstanding password request.");
            Volatile.Write(ref _status, status with { Password = GoldPasswordStatus.WaitingForResponse });
        }
    }

    /// <summary>Validate all typed payloads and transitions before committing any admission state.</summary>
    public void ProcessBatch(IReadOnlyList<GoldMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        lock (_gate)
        {
            var next = Status;
            foreach (var message in messages)
            {
                var entry = GoldGameEventDecoder.Decode(message);
                if (entry is GoldAdmissionEvent admission)
                {
                    RequireServerIdentity(message);
                    if (next.Phase != GoldAdmissionPhase.WaitingForResponse)
                        throw new InvalidDataException("Unexpected Gold FIRSTCONTACT response.");
                    next = new(GoldAdmissionPhase.SlotAssigned, admission.PlayerSlot, admission.Flags, admission.HostToken)
                    { SessionLocked = next.SessionLocked, SessionGameFlags = next.SessionGameFlags,
                        AnnouncedMaxPlayers = next.AnnouncedMaxPlayers, MaxPlayers = next.MaxPlayers,
                        SessionPaused = next.SessionPaused, PausedBy = next.PausedBy };
                }
                else if (entry is GoldClientHostRoleEvent)
                {
                    RequireServerIdentity(message);
                }
                else if (entry is GoldRejectionEvent rejection)
                {
                    RequireServerIdentity(message);
                    // 0x3F also reports receiver errors after the slot response.
                    if (next.Phase is GoldAdmissionPhase.NotRequested or GoldAdmissionPhase.Rejected)
                        throw new InvalidDataException("Unexpected Gold rejection.");
                    next = next with { Phase = GoldAdmissionPhase.Rejected, RejectionReason = rejection.Reason,
                        DataMismatchMask = rejection.DataMismatchMask };
                }
                else if (entry is GoldPasswordResultEvent password)
                {
                    RequireServerIdentity(message);
                    if (next.Phase != GoldAdmissionPhase.SlotAssigned || next.Password != GoldPasswordStatus.WaitingForResponse)
                        throw new InvalidDataException("Unexpected Gold password response.");
                    next = next with { Password = password.Accepted ? GoldPasswordStatus.Accepted : GoldPasswordStatus.Rejected };
                }
                else if (entry is GoldSessionPauseEvent pause)
                {
                    // Server relays retain the pausing player's five-bit Origin. Zero means GameServer.
                    if (message.Sender != 0 || message.Origin > 31)
                        throw new InvalidDataException("Gold pause/resume requires a server message with a five-bit origin.");
                    next = next with { SessionPaused = pause.IsPaused, PausedBy = pause.PausedBy };
                }
                else if (entry is GoldSessionLockEvent session)
                {
                    RequireServerIdentity(message);
                    next = next with { SessionLocked = session.IsLocked,
                        SessionGameFlags = next.SessionGameFlags is { } flags
                            ? session.IsLocked ? flags | GoldSessionGameFlagsCodec.LockMask : flags & ~GoldSessionGameFlagsCodec.LockMask : null };
                }
                else if (entry is GoldSessionPlayerLimitEvent limit)
                {
                    RequireServerIdentity(message);
                    next = next with { AnnouncedMaxPlayers = limit.AnnouncedMaxPlayers,
                        MaxPlayers = GoldSessionPlayerLimitCodec.Resolve(limit.AnnouncedMaxPlayers, next.SessionGameFlags, next.MaxPlayers) };
                }
                else if (entry is GoldSessionGameFlagsEvent gameFlags)
                {
                    RequireServerIdentity(message);
                    next = next with { SessionGameFlags = gameFlags.Flags, SessionLocked = (gameFlags.Flags & GoldSessionGameFlagsCodec.LockMask) != 0 };
                }
                else if (entry is GoldPlayerRosterEvent or GoldTimeProbeEvent or GoldReferenceBatchEvent or GoldReferenceRangeEvent)
                {
                    RequireServerIdentity(message);
                    if (next.Phase != GoldAdmissionPhase.SlotAssigned)
                        throw new InvalidDataException("Gold server startup message requires an assigned Sacred slot.");
                }
                else if (entry is GoldParticipantStartApprovedEvent or GoldHostWorldAvailableEvent or GoldSectorEntryEvent)
                {
                    // Native host-start broadcasts carry server sender zero and retained player origin.
                    if (message.Sender != 0 || message.Origin > 16 || next.Phase != GoldAdmissionPhase.SlotAssigned)
                        throw new InvalidDataException("Gold world startup requires a server message and an assigned Sacred slot.");
                }
                else if (entry is GoldSectorInterestEvent)
                {
                    if (message.Sender is < 1 or > 16 || message.Origin != message.Sender || next.Phase != GoldAdmissionPhase.SlotAssigned)
                        throw new InvalidDataException("Gold sector interest requires an admitted client identity.");
                }
                next = next with { WorldStartup = GoldWorldStartupReducer.Apply(next.WorldStartup, entry) };
            }
            Volatile.Write(ref _status, next);
        }
    }

    private static void RequireServerIdentity(GoldMessage message)
    {
        if (message.Sender != 0 || message.Origin != 0)
            throw new InvalidDataException("Gold admission response must originate from the server.");
    }
}
