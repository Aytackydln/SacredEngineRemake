using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Connection;

public sealed partial class GoldGameConnection
{
    private bool _worldUploadClaimed;
    private int _hostUploadFinishSlot;
    private readonly TaskCompletionSource _hostUploadApproval = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task WaitForHostUploadApprovalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAny(_hostUploadApproval.Task, Completion).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!HostUploadApproved)
            throw new IOException("The connection ended before host upload approval could be retained.");
    }

    /// <summary>Server approval for this connection's host finish. Actor and sector
    /// readiness are tracked by the caller.</summary>
    public bool HostUploadApproved
    {
        get
        {
            var slot = Volatile.Read(ref _hostUploadFinishSlot);
            var admission = Admission;
            return slot != 0 && !IsStopping && !Completion.IsCompleted &&
                admission.Phase == GoldAdmissionPhase.SlotAssigned && admission.PlayerSlot == slot &&
                admission.WorldStartup is { AssignedClientHost: true, HostWorldAvailable: true,
                    ParticipantStartApproved: false } startup && startup.AvailableHostSlot == slot;
        }
    }

    /// <summary>One startup transaction per transport lifetime, including while approval is pending.
    /// A partial transaction is never retried on the same server slot.</summary>
    internal void ClaimWorldUpload(byte slot, bool host)
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var admission = Admission;
            if (_firstContact is null || _stop.IsCancellationRequested || Completion.IsCompleted ||
                admission.Phase != GoldAdmissionPhase.SlotAssigned || admission.PlayerSlot != slot)
                throw new InvalidOperationException("World upload requires its active assigned Sacred slot.");
            if (admission.Password is GoldPasswordStatus.WaitingForResponse or GoldPasswordStatus.Rejected)
                throw new InvalidOperationException("World upload cannot precede password acceptance.");
            var startup = admission.WorldStartup;
            if (startup.AssignedClientHost != host || startup.HostWorldAvailable == host || startup.ParticipantStartApproved)
                throw new InvalidOperationException(host ? "Host upload requires the first-client host on an empty server." :
                    "Participant upload requires an available host world and a participant role.");
            if (_worldUploadClaimed) throw new InvalidOperationException("This connection has already begun its world upload.");
            _worldUploadClaimed = true;
        }
    }

    internal async Task SendWorldUploadAsync(IReadOnlyList<GoldMessage> messages, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        try
        {
            await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                // Retain the gate across begin, data and finish. Ordinary sends, time replies and
                // keepalive frames cannot split the prepared transaction's wire ordering.
                for (var index = 0; index < messages.Count; index++)
                {
                    var message = messages[index];
                    // The peer can reply before the local async write returns.
                    if (index == messages.Count - 1 && message.Opcode == GoldWorldEntryCodec.HostStartOpcode)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        lock (_lifetimeGate)
                        {
                            if (Admission.WorldStartup.HostWorldAvailable)
                                throw new InvalidOperationException("Host world availability arrived before this connection's finish.");
                            Volatile.Write(ref _hostUploadFinishSlot, message.Sender);
                        }
                    }
                    await WriteGoldMessageAsync(message, linked.Token).ConfigureAwait(false);
                }
            }
            finally { _sendGate.Release(); }
        }
        catch
        {
            Stop();
            throw;
        }
    }
}
