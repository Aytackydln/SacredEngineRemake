using Sacred.Network.Connection;

namespace Sacred.Network.Gameplay;

/// <summary>Sends prepared first-host state and the KEYX-derived finish word in one transport transaction.
/// Completion means sent. Native approval and local sector/script readiness are separate.</summary>
public sealed class GoldHostUploadSession
{
    private readonly GoldGameConnection _connection;
    private GoldHostUploadSession(GoldGameConnection connection, Task completion)
        => (_connection, Completion) = (connection, completion);
    public Task Completion { get; }
    public bool NativeApproved => Completion.IsCompletedSuccessfully && _connection.HostUploadApproved;

    /// <summary>Wait for successful transmission and server approval on this
    /// connection. Canceling the wait leaves the upload lifetime with its owner.</summary>
    public async Task WaitForApprovalAsync(CancellationToken cancellationToken = default)
    {
        await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _connection.WaitForHostUploadApprovalAsync(cancellationToken).ConfigureAwait(false);
    }

    public static GoldHostUploadSession Start(GoldGameConnection connection, GoldHostWorldUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();
        var messages = upload.CreateMessages().Prepend(GoldWorldEntryCodec.CreateHostUpload(upload.PlayerSlot))
            .Append(GoldWorldEntryCodec.CreateHostStart(upload.PlayerSlot, upload.SectorTableSize)).ToArray();
        connection.ClaimWorldUpload(upload.PlayerSlot, host: true);
        return new(connection, connection.SendWorldUploadAsync(messages, cancellationToken));
    }
}
