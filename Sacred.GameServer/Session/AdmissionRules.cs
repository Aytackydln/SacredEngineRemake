using System.Buffers.Binary;
using Sacred.Network.Admission;

namespace Sacred.GameServer.Session;

/// <summary>Rejection reasons carried by opcode 0x3F, recovered from GameServer FUN_004eb0a0 (the FIRSTCONTACT/receive handler).</summary>
public static class RejectionReason
{
    public const uint VersionMismatch = 1;      // FIRSTCONTACT version word != server TinCat/Sacred build
    public const uint DuplicateIdentity = 2;    // lobby identity already connected
    public const uint LobbyRefused = 3;
    public const uint InvalidSender = 4;        // sender slot/size mismatch
    public const uint SequenceError = 5;        // R_MESSAGE_ID_WARNING
    public const uint ChecksumOrData = 6;       // checksum error, or data fingerprint mismatch (mask in the second word)
}

/// <summary>Server side checks of a FIRSTCONTACT payload (layout constants from Sacred.Network).</summary>
public sealed class AdmissionRules
{
    private readonly GoldLanCompatibility? _expected;
    public AdmissionRules(GoldLanCompatibility? expected) => _expected = expected;
    public ushort? ExpectedVersion => _expected?.Version;

    /// <summary>Returns 0 when accepted, otherwise the rejection reason; <paramref name="mismatchMask"/> is set for data mismatches.</summary>
    public uint Check(ReadOnlySpan<byte> payload, out uint mismatchMask, out int lobbyIdentity)
    {
        mismatchMask = 0;
        lobbyIdentity = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (payload.Length != GoldFirstContactLayout.Size) return RejectionReason.InvalidSender;
        if (_expected is null) return 0; // --accept-any-data
        var version = BinaryPrimitives.ReadUInt16LittleEndian(payload[GoldFirstContactLayout.VersionOffset..]);
        if (version != _expected.Version) return RejectionReason.VersionMismatch;
        var fields = GoldDataFingerprints.Fields;
        for (var i = 0; i < GoldDataFingerprints.Count; i++)
        {
            var actual = BinaryPrimitives.ReadUInt32LittleEndian(payload[fields[i].PayloadOffset..]);
            if (actual != _expected.Fingerprints[i]) mismatchMask |= fields[i].MismatchMask;
        }
        return mismatchMask != 0 ? RejectionReason.ChecksumOrData : 0;
    }

    public static string? PlayerName(ReadOnlySpan<byte> payload)
    {
        var bytes = payload.Slice(GoldFirstContactLayout.NameOffset, GoldFirstContactLayout.NameBytes);
        var end = 0;
        while (end + 1 < bytes.Length && (bytes[end] | bytes[end + 1]) != 0) end += 2;
        return System.Text.Encoding.Unicode.GetString(bytes[..end]);
    }
}
