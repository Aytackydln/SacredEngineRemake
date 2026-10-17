using System.Buffers.Binary;
using System.Text;
using Sacred.Network.Protocol;

namespace Sacred.Network.Admission;

/// <summary>A complete native request. Unknown bytes and noncanonical flag values are retained.</summary>
public sealed class GoldFirstContact
{
    private readonly byte[] _payload;

    private GoldFirstContact(ReadOnlySpan<byte> payload) => _payload = payload.ToArray();

    public int LobbyIdentity => BinaryPrimitives.ReadInt32LittleEndian(_payload);
    public ushort Version => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldFirstContactLayout.VersionOffset));
    public byte CharacterType => _payload[GoldFirstContactLayout.CharacterTypeOffset];
    public byte ProfileByte7 => _payload[GoldFirstContactLayout.ProfileByte7Offset];
    public byte ConnectionSpeed => _payload[GoldFirstContactLayout.ConnectionSpeedOffset];
    public byte ProfileFlag9 => _payload[GoldFirstContactLayout.ProfileFlag9Offset];
    public uint ExecutableSizeToken => ReadWord(GoldFirstContactLayout.ExecutableSizeTokenOffset);
    public uint BannerFingerprint => ReadWord(GoldFirstContactLayout.BannerFingerprintOffset);
    public uint ExecutableFingerprint => ReadWord(GoldFirstContactLayout.ExecutableFingerprintOffset);
    public string Name => ReadName(_payload.AsSpan(GoldFirstContactLayout.NameOffset, GoldFirstContactLayout.NameBytes));
    public ReadOnlySpan<byte> Profile => _payload.AsSpan(GoldFirstContactLayout.ProfileOffset, GoldFirstContactLayout.ProfileBytes);
    public GoldDataFingerprints Fingerprints => GoldDataFingerprints.Read(_payload);

    public static GoldFirstContact FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldFirstContactLayout.Size)
            throw new InvalidDataException($"Gold FIRSTCONTACT requires exactly {GoldFirstContactLayout.Size} payload bytes.");
        return new GoldFirstContact(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();

    /// <summary>Reproduce native cleared-request construction using a complete saved profile and explicit client selections.</summary>
    public static GoldFirstContact Create(ReadOnlySpan<byte> profile, GoldFirstContactParameters parameters,
        GoldLanCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parameters.Name);
        ArgumentNullException.ThrowIfNull(compatibility);
        if (profile.Length != GoldFirstContactLayout.ProfileBytes)
            throw new InvalidDataException("Gold admission requires a complete 556-byte native profile.");
        var payload = new byte[GoldFirstContactLayout.Size];
        BinaryPrimitives.WriteInt32LittleEndian(payload, parameters.LobbyIdentity);
        payload[GoldFirstContactLayout.CharacterTypeOffset] = parameters.CharacterType;
        payload[GoldFirstContactLayout.ProfileByte7Offset] = parameters.ProfileByte7;
        payload[GoldFirstContactLayout.ConnectionSpeedOffset] = parameters.ConnectionSpeed;
        payload[GoldFirstContactLayout.ProfileFlag9Offset] = parameters.ProfileFlag9;
        // Native producer copies at most 256 name bytes, including an unterminated full buffer.
        var characters = Math.Min(parameters.Name.Length, GoldFirstContactLayout.NameBytes / 2);
        for (var index = 0; index < characters; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(GoldFirstContactLayout.NameOffset + index * 2), parameters.Name[index]);
        profile.CopyTo(payload.AsSpan(GoldFirstContactLayout.ProfileOffset));
        return new GoldFirstContact(payload).WithInstallation(compatibility);
    }

    /// <summary>Pre-slot identity and sequence zero match the native initial send.</summary>
    public GoldMessage CreateMessage() => new(GoldFirstContactLayout.Opcode, 0, 0, 0, ToPayload());

    /// <summary>Replaces verified compatibility fields while preserving the complete supplied profile.</summary>
    public GoldFirstContact WithCompatibility(ushort version, GoldDataFingerprints fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var payload = ToPayload();
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(GoldFirstContactLayout.VersionOffset), version);
        fingerprints.Write(payload);
        return new GoldFirstContact(payload);
    }

    private uint ReadWord(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));

    public GoldFirstContact WithInstallation(GoldLanCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        var payload = WithCompatibility(compatibility.Version, compatibility.Fingerprints).ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldFirstContactLayout.ExecutableSizeTokenOffset), compatibility.ExecutableSizeToken);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldFirstContactLayout.ExecutableFingerprintOffset), compatibility.ExecutableFingerprint);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldFirstContactLayout.BannerFingerprintOffset), compatibility.BannerFingerprint);
        return new GoldFirstContact(payload);
    }

    internal static string ReadName(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset < bytes.Length; offset += 2)
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) == 0)
                return Encoding.Unicode.GetString(bytes[..offset]);
        return Encoding.Unicode.GetString(bytes);
    }
}
