using Sacred.Network.Connection;

namespace Sacred.Network.Gameplay;

/// <summary>One ordered participant upload. Protocol approval arrives separately through the connection.</summary>
public sealed class GoldParticipantUploadSession
{
    private GoldParticipantUploadSession(Task completion) => Completion = completion;
    public Task Completion { get; }

    public static GoldParticipantUploadSession Start(GoldGameConnection connection, GoldParticipantCharacterUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();
        // Validate/copy the whole data sequence before the first control is sent.
        var messages = upload.CreateMessages().Prepend(GoldWorldEntryCodec.CreateParticipantUpload(upload.PlayerSlot))
            .Append(GoldWorldEntryCodec.CreateParticipantStart(upload.PlayerSlot)).ToArray();
        connection.ClaimWorldUpload(upload.PlayerSlot, host: false);
        return new(connection.SendWorldUploadAsync(messages, cancellationToken));
    }
}
