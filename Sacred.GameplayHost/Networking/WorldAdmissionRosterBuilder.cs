using System.Buffers.Binary;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold accepted 0x11 roster stage through 0x7E15AF submission.
/// String-array assignment and consumer/UI continuation remain separate services.</summary>
public static class WorldAdmissionRosterBuilder
{
    public static WorldAdmissionRosterStage Build(WorldAdmissionControllerPrefixResult prefix,
        GoldAdmissionEvent admission, GoldPlayerData entryZero, WorldAdmissionRosterInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(entryZero);
        ArgumentNullException.ThrowIfNull(inputs);
        if (prefix.Rejected) throw new InvalidOperationException("Rejected admission has no accepted roster stage.");
        var decoded = GoldGameEventDecoder.Decode(admission.Message) as GoldAdmissionEvent
            ?? throw new InvalidDataException("Admission roster requires the original 0x11 response.");
        if (prefix.State.LocalReference != decoded.PlayerSlot)
            throw new InvalidOperationException("Admission roster inputs belong to a different prefix identity.");
        var bytes = new byte[GoldPlayerRosterLayout.Size];
        entryZero.ToPayload().CopyTo(bytes, 0);
        var at = decoded.PlayerSlot * GoldPlayerDataLayout.Size;
        var name = inputs.LocalName.Span;
        name[..Math.Min(name.Length & ~1, GoldPlayerDataLayout.NameBytes)].CopyTo(bytes.AsSpan(at + GoldPlayerDataLayout.NameOffset));
        entryZero.Profile.CopyTo(bytes.AsSpan(at + GoldPlayerDataLayout.ProfileOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + GoldPlayerDataLayout.SessionWord0Offset), inputs.SessionWord0);
        bytes[at + GoldPlayerDataLayout.CharacterTypeOffset] = inputs.CharacterType;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + GoldPlayerDataLayout.LobbyIdentityOffset), decoded.PlayerSlot);
        var replace = (decoded.Flags & 8) != 0;
        if (replace)
        {
            var replacement = inputs.ReplacementName.Span;
            var count = replacement.Length & ~1;
            if (count > bytes.Length - at - GoldPlayerDataLayout.NameOffset)
                throw new NotSupportedException("Native admission name copy extends beyond the roster submission boundary.");
            // Native does not clamp this second copy to 256 bytes. It can
            // overwrite profile bytes; do not silently normalize that behavior.
            replacement[..count].CopyTo(bytes.AsSpan(at + GoldPlayerDataLayout.NameOffset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + GoldPlayerDataLayout.LobbyIdentityOffset), inputs.AlternateLobbyIdentity);
            bytes[at + GoldPlayerDataLayout.ProfileByte5Offset] = inputs.ProfileByte5;
        }
        return new(GoldPlayerRoster.FromPayload(bytes), (byte)((inputs.ConnectionFlags & 2) == 0 ? 1 : 0), (byte)(replace ? 1 : 0));
    }
}
