using Sacred.Network.Admission;
using Sacred.Network.Connection;

namespace Sacred.Network.Gameplay;

/// <summary>Sends prepared first-host state and the KEYX-derived finish word in one transport transaction.
/// Completion means sent. Native approval and local sector/script readiness are separate.</summary>
public sealed class GoldHostUploadSession
{
    private readonly GoldGameConnection _connection;
    private readonly byte _slot;
    private GoldHostUploadSession(GoldGameConnection connection, byte slot, Task completion)
        => (_connection, _slot, Completion) = (connection, slot, completion);
    public Task Completion { get; }
    public bool NativeApproved => Completion.IsCompletedSuccessfully && !_connection.IsStopping && !_connection.Completion.IsCompleted &&
        _connection.Admission is { Phase: GoldAdmissionPhase.SlotAssigned } admission && admission.PlayerSlot == _slot &&
        admission.WorldStartup.AssignedClientHost && admission.WorldStartup.HostWorldAvailable &&
        admission.WorldStartup.AvailableHostSlot == _slot;

    public static GoldHostUploadSession Start(GoldGameConnection connection, GoldHostWorldUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();
        var messages = upload.CreateMessages().Prepend(GoldWorldEntryCodec.CreateHostUpload(upload.PlayerSlot))
            .Append(GoldWorldEntryCodec.CreateHostStart(upload.PlayerSlot, upload.SectorTableSize)).ToArray();
        connection.ClaimWorldUpload(upload.PlayerSlot, host: true);
        return new(connection, upload.PlayerSlot, connection.SendWorldUploadAsync(messages, cancellationToken));
    }
}
